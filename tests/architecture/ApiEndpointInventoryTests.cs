namespace VibeChat.ArchitectureTests;

public sealed class ApiEndpointInventoryTests
{
    [Fact]
    public void Empty_inventory_fails_instead_of_approving_composition_root()
    {
        Assert.Throws<InvalidOperationException>(() => Read("v1.MapIdentity();"));
    }

    [Theory]
    [InlineData("Get")]
    [InlineData("Post")]
    public void Following_endpoint_permission_does_not_cover_an_unguarded_mutation(string nextVerb)
    {
        var maps = Read("v1.MapPost(\"/unguarded\", Handler);\n"
            + $"v1.Map{nextVerb}(\"/guarded\", Handler).RequirePermission(Permissions.Message.Read);");

        Assert.Equal(["Endpoints/TestEndpoints.cs: POST /unguarded"],
            ApiEndpointInventory.MissingPermissionDeclarations(maps));
    }

    [Fact]
    public void Permissions_do_not_cross_source_file_boundaries()
    {
        var maps = ApiEndpointInventory.Read(new Dictionary<string, string>
        {
            ["Endpoints/Nested/TestEndpoints.cs"] = "v1.MapPost(\"/unguarded\", Handler);",
            ["GroupDmEndpoints.cs"] = "v1.MapPost(\"/group\", Handler).AllowPermissionGateExempt(\"membership\");"
        });

        Assert.Single(ApiEndpointInventory.MissingPermissionDeclarations(maps));
        Assert.Equal(2, maps.Length);
    }

    [Fact]
    public void Comments_handler_strings_and_later_helpers_are_not_permission_metadata()
    {
        var maps = Read("v1.MapPost(\"/unguarded\", () => \".RequirePermission(fake)\");\n"
            + "// .AllowPermissionGateExempt(fake)\n"
            + "void Other() { builder.RequirePermission(Permissions.Message.Read); }");

        Assert.Single(ApiEndpointInventory.MissingPermissionDeclarations(maps));
    }

    [Fact]
    public void Commented_maps_do_not_populate_the_inventory()
    {
        Assert.Throws<InvalidOperationException>(() => Read("// v1.MapPost(\"/fake\", Handler);"));
    }

    [Fact]
    public void Matrix_requires_the_method_and_path_not_just_a_mention()
    {
        var maps = Read("v1.MapGet(\"/known\", Handler);\n"
            + "v1.MapPost(\"/known\", Handler);\n"
            + "v1.MapGet(\"/new\", Handler);");
        const string matrix = "| GET | `/known` | membership |\nText mentioning `/new` is not a row.";

        Assert.Equal(["Endpoints/TestEndpoints.cs: POST /known", "Endpoints/TestEndpoints.cs: GET /new"],
            ApiEndpointInventory.MissingMatrixEntries(maps, matrix));
    }

    private static ApiEndpointMap[] Read(string source) => ApiEndpointInventory.Read(
        new Dictionary<string, string> { ["Endpoints/TestEndpoints.cs"] = source });
}
