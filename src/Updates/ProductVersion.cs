internal static class ProductVersion
{
    internal static string Value => typeof(ProductVersion).Assembly.GetName().Version!.ToString(3);
}

