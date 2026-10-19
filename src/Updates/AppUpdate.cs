#nullable disable
// Adapted from Standalone DLSS Glass Cockpit Refresh; see NOTICE.md and notices/UPDATER-LICENSE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

// Updates only signed application files. User configuration and provider settings are never managed.
internal static class AppUpdate
{
    internal const string Product = "WSG-OpenXR-FG-Switcher";
    internal const string Exe = "WSG OpenXR FG Switcher.exe", HelperExe = "WSG OpenXR FG Switcher.Updater.exe", Inventory = "app-files.json", StageName = ".app-update";
    internal const string Server = "https://wesimglide.org/otherdata/tools/";
    internal const string Feed = Server + Product + ".version.json", Archive = Server + Product + ".zip";
    const long MaxZip = 256 * 1024 * 1024, MaxExpanded = 768 * 1024 * 1024;
    internal static readonly string[] ManagedNames = {
        Exe, "WSG OpenXR FG Switcher.dll", "WSG OpenXR FG Switcher.deps.json", "WSG OpenXR FG Switcher.runtimeconfig.json",
        HelperExe, "WSG OpenXR FG Switcher.Updater.dll", "WSG OpenXR FG Switcher.Updater.deps.json", "WSG OpenXR FG Switcher.Updater.runtimeconfig.json",
        "WSG OpenXR FG Switcher.LayerHelper.exe", "WSG OpenXR FG Switcher.LayerHelper.dll",
        "WSG OpenXR FG Switcher.LayerHelper.deps.json", "WSG OpenXR FG Switcher.LayerHelper.runtimeconfig.json",
        "Wsg.OpenXR.Core.dll", "VERSION", "README.md", "NOTICE.md", "UPDATER-LICENSE.md",
        "LICENSE.md"
    };
    internal static readonly string[] HelperFiles = { HelperExe, "WSG OpenXR FG Switcher.Updater.dll", "WSG OpenXR FG Switcher.Updater.deps.json", "WSG OpenXR FG Switcher.Updater.runtimeconfig.json" };
    internal static bool Enabled {
        get {
#if WSG_NO_AUTO_UPDATE
            return false;
#else
            return true;
#endif
        }
    }
    internal sealed class Release {
        public string signature { get; set; }
        public string keyId { get; set; }
        public int schema { get; set; }
        public string product { get; set; }
        public string version { get; set; }
        public string sha256 { get; set; }
        public long size { get; set; }
    }
    internal sealed class Files {
        public string signature { get; set; }
        public string keyId { get; set; }
        public int schema { get; set; }
        public string product { get; set; }
        public string version { get; set; }
        public Dictionary<string,string> files { get; set; }
    }
    internal sealed class Change { public string name {get;set;} public string before {get;set;} public string after {get;set;} }
    internal sealed class Journal { public string oldInventory {get;set;} public string newInventory {get;set;} public List<Change> changes {get;set;} }
    static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 16, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    internal static string Json(object value) { return JsonSerializer.Serialize(value, JsonOptions); }
    static T Read<T>(string path) { Require(new FileInfo(path).Length<=1024*1024,"Update metadata is too large."); return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions); }
    static void Require(bool ok,string message) { if(!ok)throw new InvalidDataException(message); }
    internal static string Hash(byte[] bytes) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
    internal static string HashFile(string path) { using(var f=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant(); }
    static bool IsHash(string s) { return s!=null&&Regex.IsMatch(s,"\\A[0-9a-f]{64}\\z"); }
    internal static Version ParseVersion(string s) {
        Require(s!=null&&Regex.IsMatch(s,"\\A(0|[1-9][0-9]{0,4})\\.(0|[1-9][0-9]{0,4})\\.(0|[1-9][0-9]{0,4})\\z"),"Invalid release version.");
        var v=new Version(s);return new Version(v.Major,v.Minor,v.Build,0);
    }
    internal static Release ParseRelease(byte[] bytes) {
        Require(bytes.Length<=65536,"Version file is too large.");
        var r=JsonSerializer.Deserialize<Release>(bytes, JsonOptions);
        Require(r!=null&&r.schema==1&&r.product==Product&&IsHash(r.sha256)&&r.size>0&&r.size<=MaxZip,"Invalid standalone version file.");
        ParseVersion(r.version);VerifyRelease(r);return r;
    }
    static void VerifyRelease(Release r) { ReleaseTrust.Verify(ReleaseTrust.ReleaseMessage(r.product,r.version,r.sha256,r.size),r.signature,r.keyId); }
    internal static void CheckUrl(Uri url) { Require(url.AbsoluteUri==Feed||url.AbsoluteUri==Archive,"Unexpected update address."); }
    internal static byte[] Download(Uri url,long limit) {
        CheckUrl(url);
        using(var handler=new System.Net.Http.HttpClientHandler { AllowAutoRedirect=false,UseCookies=false })
        using(var client=new System.Net.Http.HttpClient(handler))
        using(var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(30))) {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(Product+"/"+ProductVersion.Value);
            client.DefaultRequestHeaders.CacheControl=new System.Net.Http.Headers.CacheControlHeaderValue { NoCache=true,NoStore=true };
            using(var response=client.GetAsync(url,System.Net.Http.HttpCompletionOption.ResponseHeadersRead,cancellation.Token).GetAwaiter().GetResult()) {
                response.EnsureSuccessStatusCode();
                Require(response.StatusCode==HttpStatusCode.OK&&(!response.Content.Headers.ContentLength.HasValue||response.Content.Headers.ContentLength<=limit),"Update server did not return a valid file.");
                using(var input=response.Content.ReadAsStreamAsync(cancellation.Token).GetAwaiter().GetResult())using(var output=new MemoryStream()) {
                    var buffer=new byte[65536];int n;
                    while((n=input.ReadAsync(buffer,0,buffer.Length,cancellation.Token).GetAwaiter().GetResult())>0) {
                        Require(output.Length+n<=limit,"Update download exceeded its limit.");output.Write(buffer,0,n);
                    }
                    return output.ToArray();
                }
            }
        }
    }
    static void NoLinks(string path) {
        path=Path.GetFullPath(path);Require(!path.StartsWith(@"\\"),"Use a local app folder.");
        for(string p=path;!String.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))
            if(File.Exists(p)||Directory.Exists(p))Require((File.GetAttributes(p)&FileAttributes.ReparsePoint)==0,"Linked update paths are not supported.");
    }
    internal static string SafePath(string root,string name) {
        Require(!String.IsNullOrEmpty(name)&&name.Length<=220&&!name.Contains('\\')&&!Path.IsPathRooted(name),"Invalid update filename.");
        foreach(var part in name.Split('/')) {
            Require(part.Length>0&&part!="."&&part!=".."&&part.TrimEnd(' ','.')==part&&part.IndexOfAny(Path.GetInvalidFileNameChars())<0,"Unsafe update filename.");
            Require(!Regex.IsMatch(part.Split('.')[0],"\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\\z",RegexOptions.IgnoreCase),"Reserved update filename.");
        }
        Require(!name.Split('/')[0].Equals(StageName,StringComparison.OrdinalIgnoreCase),"Update archive uses its working folder.");
        var path=Path.GetFullPath(Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)));
        Require(path.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase),"Update path leaves the app folder.");
        NoLinks(root);var current=root;foreach(var part in name.Split('/')) { current=Path.Combine(current,part);NoLinks(current); }return path;
    }
    static string Stage(string root) { NoLinks(root);var s=Path.Combine(root,StageName);NoLinks(s);return s; }
    static string Owner(string root) { return Product+"\n"+Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant(); }
    static void CheckTree(string path) {
        NoLinks(path);
        // Check each directory before recursing, never follow a junction.
        foreach(var p in Directory.GetFileSystemEntries(path)) { NoLinks(p);if(Directory.Exists(p))CheckTree(p); }
    }
    static void CheckStage(string root) {
        string s=Stage(root);NoLinks(Path.Combine(s,"owner"));
        Require(File.ReadAllText(Path.Combine(s,"owner"))==Owner(root),"Unrecognized update working folder; preserved.");CheckTree(s);
    }
    internal sealed class Gate : IDisposable {
        Mutex mutex;internal Gate(Mutex m) { mutex=m; }
        public void Dispose() { if(mutex!=null) { mutex.ReleaseMutex();mutex.Dispose();mutex=null; } }
    }
    internal static Gate Enter(string root) {
        string id=Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant())).Substring(0,32);
        var m=new Mutex(false,"Local\\WsgOpenXrFgSwitcherApp."+id);bool held=false;
        try { held=m.WaitOne(0); }catch(AbandonedMutexException) { held=true; }
        if(!held) { m.Dispose();return null; }return new Gate(m);
    }
    internal static Files ReadInventory(string root,bool verify) {
        var f=Read<Files>(SafePath(root,Inventory));
        Require(f!=null&&f.schema==1&&f.product==Product&&f.files!=null&&f.files.Count>0&&f.files.Count<1000,"Invalid standalone app inventory.");
        ReleaseTrust.Verify(ReleaseTrust.InventoryMessage(f.product,f.version,f.files),f.signature,f.keyId);
        ParseVersion(f.version);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in f.files) {
            string p=SafePath(root,pair.Key);
            Require(IsManagedName(pair.Key)&&seen.Add(pair.Key)&&IsHash(pair.Value),"Invalid or user-owned inventory entry.");
            if(verify)Require(File.Exists(p)&&HashFile(p)==pair.Value,"App file changed or missing: "+pair.Key+". Extract a fresh ZIP to update.");
        }
        foreach(string n in ManagedNames)Require(f.files.ContainsKey(n),"Incomplete app package: "+n);
        return f;
    }
    static void Clean(string root) {
        string s=Stage(root);if(!Directory.Exists(s))return;CheckStage(root);
        // Never partially remove the owner/recovery markers while our helper is running.
        try {
            string helper=Path.Combine(s,HelperExe);
            if(File.Exists(helper))using(var probe=new FileStream(helper,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {}
            foreach(string p in Directory.GetFiles(s,"*",SearchOption.AllDirectories))
                if(p!=Path.Combine(s,"owner")&&p!=Path.Combine(s,"journal.json")&&p!=Path.Combine(s,"finished"))File.Delete(p);
            foreach(string p in Directory.GetDirectories(s,"*",SearchOption.AllDirectories).OrderByDescending(p=>p.Length))Directory.Delete(p);
            foreach(string n in new[]{"journal.json","finished","owner"})if(File.Exists(Path.Combine(s,n)))File.Delete(Path.Combine(s,n));
            Directory.Delete(s);
        }catch(IOException) {}catch(UnauthorizedAccessException) {}
    }
    // Injectable retrieval is for compiled offline tests, never a CLI/configurable server override.
    internal static bool Prepare(string root,Release release,Func<Uri,long,byte[]> get) {
        VerifyRelease(release);
        Require(!File.Exists(Path.Combine(root,"FlightSimulator2024.exe")),"Keep the app package outside the MSFS Content folder.");
        var old=ReadInventory(root,true);Require(ParseVersion(release.version)>ParseVersion(old.version),"Update is not newer than this package.");
        byte[] zip=get(new Uri(Archive),release.size);
        Require(zip.LongLength==release.size&&Hash(zip)==release.sha256,"Update checksum mismatch; current version kept.");
        string stage=Stage(root);
        if(Directory.Exists(stage)) { Clean(root);Require(!Directory.Exists(stage),"An updater is still finishing. Try next launch."); }
        Directory.CreateDirectory(stage);File.WriteAllText(Path.Combine(stage,"owner"),Owner(root));
        try {
            string next=Path.Combine(stage,"new");Directory.CreateDirectory(next);
            using(var stream=new MemoryStream(zip))using(var archive=new ZipArchive(stream,ZipArchiveMode.Read)) {
                long total=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);Require(archive.Entries.Count<1000,"Too many update files.");
                foreach(var entry in archive.Entries) {
                    Require(!entry.FullName.EndsWith("/"),"Unexpected directory entry in release ZIP.");
                    string path=SafePath(next,entry.FullName);Require(seen.Add(entry.FullName),"Duplicate update filename.");
                    Require(((entry.ExternalAttributes>>16)&0xF000)!=0xA000&&(entry.ExternalAttributes&0x400)==0,"Linked ZIP entries are not supported.");
                    Require(entry.Length>=0&&entry.Length<=MaxExpanded-total,"Expanded update exceeds its limit.");total+=entry.Length;Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using(var input=entry.Open())using(var output=new FileStream(path,FileMode.CreateNew)) {
                        var buffer=new byte[65536];int n;long written=0;
                        while((n=input.Read(buffer,0,buffer.Length))>0) { written+=n;Require(written<=entry.Length,"Invalid ZIP length.");output.Write(buffer,0,n); }
                        Require(written==entry.Length,"Truncated ZIP entry.");
                    }
                }
            }
            var fresh=ReadInventory(next,true);Require(fresh.version==release.version,"ZIP and version file disagree.");
            Require(Directory.GetFiles(next,"*",SearchOption.AllDirectories).Length==fresh.files.Count+1,"ZIP contains unlisted files.");
            Require(File.ReadAllText(Path.Combine(next,"VERSION")).Trim()==release.version,"Package version mismatch.");
            foreach(string n in fresh.files.Keys) { string dest=SafePath(root,n);Require(old.files.ContainsKey(n)||(!File.Exists(dest)&&!Directory.Exists(dest)),"Update would replace unrelated data: "+n); }
            File.WriteAllText(Path.Combine(stage,"release.json"),Json(release));
            File.Copy(Path.Combine(root,Inventory),Path.Combine(stage,"source-inventory.json"));
            // Copy this locally trusted updater, not code downloaded from the server.
            foreach(var name in old.files.Keys)File.Copy(Path.Combine(root,name),Path.Combine(stage,name));return true;
        }catch { Clean(root);throw; }
    }
    internal static async Task<bool> CheckAsync(string root,Action<string> status,Func<bool> canRestart,string output,Func<bool> minimized) {
#if WSG_NO_AUTO_UPDATE
        status("Source build: automatic app updates disabled.");return await Task.FromResult(false);
#else
        if(new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) {
            status("Open the app normally (not as administrator) for automatic updates.");return false;
        }
        return await CheckForUpdateAsync(root,status,canRestart,Download,()=>Launch(root,false,output,minimized()));
#endif
    }
    internal static async Task<bool> CheckForUpdateAsync(string root,Action<string> status,Func<bool> canRestart,Func<Uri,long,byte[]> get,Action launch) {
        try {
            status("Checking for app updates...");var release=await Task.Run(()=>ParseRelease(get(new Uri(Feed),65536)));
            if(ParseVersion(release.version)<=ParseVersion(ProductVersion.Value)) { status("App is up to date.");return false; }
            status("Downloading app v"+release.version+"...");await Task.Run(()=>Prepare(root,release,get));
            if(!canRestart()) { status("Update ready — restart when all games are closed.");return false; }
            status("Updating app and reopening...");launch();return true;
        }catch(InvalidDataException) { status("Update verification failed; current version kept.");return false; }
        catch(Exception) { status("App update unavailable; current version kept. Checks again next launch.");return false; }
    }
    static void AtomicCopy(string source,string destination,string temp,string expected) {
        Directory.CreateDirectory(Path.GetDirectoryName(destination));if(File.Exists(temp))File.Delete(temp);File.Copy(source,temp);
        Require(HashFile(temp)==expected,"Update copy changed; replacement stopped.");
        if(File.Exists(destination))File.Replace(temp,destination,null);else File.Move(temp,destination);
    }
    internal static void Recover(string root) {
        string s=Stage(root);CheckStage(root);string journal=Path.Combine(s,"journal.json");
        if(!File.Exists(journal)) { Clean(root);return; }
        var j=Read<Journal>(journal);Require(j!=null&&IsHash(j.oldInventory)&&IsHash(j.newInventory)&&j.changes!=null,"Invalid app recovery record.");
        string inventory=SafePath(root,Inventory);
        var source=Read<Files>(Path.Combine(s,"source-inventory.json"));
        ReleaseTrust.Verify(ReleaseTrust.InventoryMessage(source.product,source.version,source.files),source.signature,source.keyId);
        Require(HashFile(Path.Combine(s,"source-inventory.json"))==j.oldInventory,"Recovery origin changed.");
        var fresh=ReadInventory(Path.Combine(s,"new"),false);
        Require(HashFile(Path.Combine(s,"new",Inventory))==j.newInventory,"Recovery destination changed.");
        var expected=new HashSet<string>(source.files.Keys,StringComparer.OrdinalIgnoreCase);expected.UnionWith(fresh.files.Keys);
        Require(j.changes.Count==expected.Count,"Recovery entry count changed.");
        foreach(var c in j.changes) {
            string before=null,after=null;source.files.TryGetValue(c.name,out before);fresh.files.TryGetValue(c.name,out after);
            Require(expected.Remove(c.name)&&c.before==before&&c.after==after,"Recovery record does not match signed inventories.");
        }
        if(File.Exists(inventory)&&HashFile(inventory)==j.newInventory) { Clean(root);return; }
        Require(File.Exists(inventory)&&HashFile(inventory)==j.oldInventory,"App inventory changed; recovery files preserved.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var c in j.changes) {
            string dest=SafePath(root,c.name);Require(names.Add(c.name)&&!c.name.Equals(Inventory,StringComparison.OrdinalIgnoreCase)&&(c.before==null||IsHash(c.before))&&(c.after==null||IsHash(c.after)),"Invalid recovery entry.");
            string current=File.Exists(dest)?HashFile(dest):null;Require(current==c.before||current==c.after,"Changed app file preserved during recovery: "+c.name);
            if(c.before!=null&&current!=c.before)Require(HashFile(SafePath(Path.Combine(s,"old"),c.name))==c.before,"Damaged app recovery copy.");
        }
        foreach(var c in j.changes) {
            string dest=SafePath(root,c.name);if(File.Exists(dest)&&HashFile(dest)==c.before)continue;
            if(c.before==null) { if(File.Exists(dest))File.Delete(dest); }
            else AtomicCopy(SafePath(Path.Combine(s,"old"),c.name),dest,Path.Combine(s,"restore.tmp"),c.before);
        }
        File.WriteAllText(Path.Combine(s,"finished"),"recovered");Clean(root);
    }
    internal static void Apply(string root,Action<int> afterWrite) {
        string s=Stage(root);CheckStage(root);Require(!File.Exists(Path.Combine(s,"journal.json")),"Recover the interrupted update first.");
        var release=Read<Release>(Path.Combine(s,"release.json"));var old=ReadInventory(root,true);var fresh=ReadInventory(Path.Combine(s,"new"),true);
        VerifyRelease(release);
        Require(release.version==fresh.version&&ParseVersion(fresh.version)>ParseVersion(old.version),"Staged version mismatch.");
        var j=new Journal { oldInventory=HashFile(Path.Combine(root,Inventory)),newInventory=HashFile(Path.Combine(s,"new",Inventory)),changes=new List<Change>() };
        var names=new HashSet<string>(old.files.Keys,StringComparer.OrdinalIgnoreCase);names.UnionWith(fresh.files.Keys);
        foreach(string name in names.OrderBy(n=>n,StringComparer.OrdinalIgnoreCase)) {
            string before=null,after=null;old.files.TryGetValue(name,out before);fresh.files.TryGetValue(name,out after);string dest=SafePath(root,name);
            Require(before!=null||(!File.Exists(dest)&&!Directory.Exists(dest)),"New file conflicts with unrelated app data.");j.changes.Add(new Change {name=name,before=before,after=after});
            if(before!=null) { string saved=SafePath(Path.Combine(s,"old"),name);Directory.CreateDirectory(Path.GetDirectoryName(saved));File.Copy(dest,saved);Require(HashFile(saved)==before,"App changed during staging."); }
        }
        File.WriteAllText(Path.Combine(s,"journal.tmp"),Json(j));File.Move(Path.Combine(s,"journal.tmp"),Path.Combine(s,"journal.json"));
        try {
            int index=0;
            foreach(var c in j.changes) {
                string dest=SafePath(root,c.name);Require((File.Exists(dest)?HashFile(dest):null)==c.before,"App changed during update.");
                if(c.after==null)File.Delete(dest);else AtomicCopy(SafePath(Path.Combine(s,"new"),c.name),dest,Path.Combine(s,"replace.tmp"),c.after);
                if(afterWrite!=null)afterWrite(++index);
            }
            Require(HashFile(Path.Combine(root,Inventory))==j.oldInventory,"App inventory changed during update.");
            AtomicCopy(Path.Combine(s,"new",Inventory),Path.Combine(root,Inventory),Path.Combine(s,"commit.tmp"),j.newInventory);if(afterWrite!=null)afterWrite(-1);
        }catch { Recover(root);throw; }
        File.WriteAllText(Path.Combine(s,"finished"),"committed");Clean(root);
    }
    internal sealed class RestartOptions { public string output {get;set;} public bool minimized {get;set;} }
    internal static void Launch(string root,bool recover,string output=null,bool minimized=false) {
        CheckStage(root);var self=Process.GetCurrentProcess();string helper=Path.Combine(Stage(root),HelperExe);Require(File.Exists(helper),"Update helper is missing. Extract a fresh package.");
        VerifyHelper(root,helper);
        File.WriteAllText(Path.Combine(Stage(root),"restart.json"),Json(new RestartOptions { output=output,minimized=minimized }));
        Process.Start(new ProcessStartInfo(helper,(recover?"--recover-app-update ":"--apply-app-update ")+self.Id+" "+self.StartTime.ToUniversalTime().Ticks) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root });
    }
    internal static bool Startup(string root,string output=null,bool minimized=false) {
#if WSG_NO_AUTO_UPDATE
        return false;
#else
        if(new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))return false;
        if(!Directory.Exists(Stage(root)))return false;CheckStage(root);string journal=Path.Combine(Stage(root),"journal.json");
        if(File.Exists(Path.Combine(Stage(root),"finished"))) { Clean(root);return false; }
        if(File.Exists(journal)) { var j=Read<Journal>(journal);if(HashFile(SafePath(root,Inventory))!=j.newInventory) { Launch(root,true,output,minimized);return true; } }
        else if(File.Exists(Path.Combine(Stage(root),"release.json"))&&File.Exists(Path.Combine(Stage(root),HelperExe))) {
            var release=Read<Release>(Path.Combine(Stage(root),"release.json"));
            VerifyRelease(release);
            if(ParseVersion(release.version)>ParseVersion(ReadInventory(root,true).version)) { Launch(root,false,output,minimized);return true; }
        }
        Clean(root);return false;
#endif
    }
    internal static int Helper(string[] args) {
        Require(!new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator),"Run automatic updates without administrator rights.");
        Require(args.Length==3&&(args[0]=="--apply-app-update"||args[0]=="--recover-app-update"),"Invalid updater arguments.");string stage=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');Require(Path.GetFileName(stage)==StageName,"Run the app normally to update it.");
        string root=Directory.GetParent(stage).FullName;CheckStage(root);VerifyHelper(root,Path.Combine(stage,HelperExe));int pid=Int32.Parse(args[1]);long ticks=Int64.Parse(args[2]);
        var restart=Read<RestartOptions>(Path.Combine(stage,"restart.json"));
        try { using(var parent=Process.GetProcessById(pid))if(parent.StartTime.ToUniversalTime().Ticks==ticks)Require(parent.WaitForExit(30000),"Close the previous app window to finish updating."); }catch(ArgumentException) {}
        using(var gate=Enter(root)) {
            Require(gate!=null,"Another app window is open; update postponed.");
            try { if(args[0]=="--recover-app-update")Recover(root);else Apply(root,null); }
            catch { if(Directory.Exists(stage)&&!File.Exists(Path.Combine(stage,"journal.json")))File.WriteAllText(Path.Combine(stage,"finished"),"not applied");throw; }
        }
        var start=new ProcessStartInfo(Path.Combine(root,Exe)) { UseShellExecute=false,WorkingDirectory=root };
        if(!string.IsNullOrEmpty(restart.output)) { start.ArgumentList.Add("--output");start.ArgumentList.Add(restart.output); }
        if(restart.minimized)start.ArgumentList.Add("--minimized");
        Process.Start(start);return 0;
    }
    static void VerifyHelper(string root,string helper) {
        var original=Read<Files>(Path.Combine(Stage(root),"source-inventory.json"));
        Require(original!=null&&original.product==Product&&original.schema==1,"Invalid updater origin.");
        ReleaseTrust.Verify(ReleaseTrust.InventoryMessage(original.product,original.version,original.files),original.signature,original.keyId);
        foreach(var name in original.files.Keys)
            Require(original.files.ContainsKey(name)&&HashFile(SafePath(Path.GetDirectoryName(helper),name))==original.files[name],"Updater helper changed; replacement stopped.");
    }
    // Signed legacy runtime files remain recognized so upgrades can remove them.
    static bool IsManagedName(string name) {
        if(name.Contains('/')||name.Contains('\\'))return false;
        if(name.Equals("settings.json",StringComparison.OrdinalIgnoreCase)||name.Equals("notes.json",StringComparison.OrdinalIgnoreCase)||name.Equals("recovery.json",StringComparison.OrdinalIgnoreCase))return false;
        if(ManagedNames.Contains(name,StringComparer.Ordinal))return true;
        return new[]{".dll",".exe",".json",".dat",".bin",".txt"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase)
            && !name.Equals(Inventory,StringComparison.OrdinalIgnoreCase);
    }
}

