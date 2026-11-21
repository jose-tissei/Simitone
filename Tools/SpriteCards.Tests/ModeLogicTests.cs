using System.Linq;
using Xunit;

public class ModeLogicTests
{
    static ModeDef Mode(string id) { return ModeLogic.Find(id); }

    static ModelLists Lists(string[] list = null, string[] user = null)
    {
        var l = new ModelLists();
        ModeLogic.ParseList(list ?? new string[0], l);
        ModeLogic.ParseOverrides(user ?? new string[0], l);
        return l;
    }

    // ---- compatibility table -------------------------------------------------------------------------
    [Fact]
    public void ThereAreExactlyFourModes()
    {
        Assert.Equal(new[] { "2d", "list", "arch", "full3d" }, ModeLogic.Modes.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void ModeNamesAreTheOnesTheUserAskedFor()
    {
        Assert.Equal(new[] { "2D mode", "3D with 2D list", "3D arch + 2D objects", "Full 3D" }, ModeLogic.Modes.Select(m => m.Label).ToArray());
    }

    [Fact]
    public void OnlyTheTwoHybridModesMixSpritesAndMeshes()
    {
        Assert.Equal(new[] { "list", "arch" }, ModeLogic.Modes.Where(m => m.Hybrid).Select(m => m.Id).ToArray());
    }

    [Theory]
    [InlineData("fixed", "list")]
    [InlineData("classic", "arch")]
    [InlineData("free", "full3d")]
    [InlineData("2d", "2d")]
    [InlineData("  list \r\n", "list")]
    public void FindAcceptsCurrentAndLegacyIds(string input, string expected)
    {
        Assert.Equal(expected, ModeLogic.Find(input).Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    public void FindRejectsUnknownIds(string input)
    {
        Assert.Null(ModeLogic.Find(input));
    }

    // ---- per-object decisions ------------------------------------------------------------------------
    [Fact]
    public void ListMode_ObjectsAreMeshesUnlessListed()
    {
        var lists = Lists(new[] { "clownportal", "spellbook" });
        Assert.True(ModeLogic.UsesSprite(Mode("list"), "clownportal", lists));
        Assert.True(ModeLogic.UsesSprite(Mode("list"), "spellbook", lists));
        Assert.False(ModeLogic.UsesSprite(Mode("list"), "fridges", lists));
    }

    [Fact]
    public void ListMode_PrefixEntriesMatchTreesAndPlants()
    {
        var lists = Lists(new[] { "trees*", "plants*", "flowers*" });
        foreach (var n in new[] { "trees", "trees2", "plants", "plants2", "plantsspellbound", "flowersoutdoor" })
            Assert.True(ModeLogic.UsesSprite(Mode("list"), n, lists), n);
        Assert.False(ModeLogic.UsesSprite(Mode("list"), "treehouse2", lists)); // "tree" is not "trees"
    }

    [Fact]
    public void ListMode_MeshLinesBeatSpriteLines()
    {
        var lists = Lists(new[] { "trees*", "mesh: treesspecial" });
        Assert.False(ModeLogic.UsesSprite(Mode("list"), "treesspecial", lists));
        Assert.True(ModeLogic.UsesSprite(Mode("list"), "trees", lists));
    }

    [Fact]
    public void ArchMode_ObjectsAreSprites_ExceptDoorsWindowsAndMeshLines()
    {
        var lists = Lists(new[] { "clownportal", "mesh: statue" });
        Assert.True(ModeLogic.UsesSprite(Mode("arch"), "fridges", lists));
        Assert.True(ModeLogic.UsesSprite(Mode("arch"), "clownportal", lists));   // the sprite list is irrelevant here
        Assert.False(ModeLogic.UsesSprite(Mode("arch"), "doors", lists));
        Assert.False(ModeLogic.UsesSprite(Mode("arch"), "doors6", lists));
        Assert.False(ModeLogic.UsesSprite(Mode("arch"), "windows", lists));
        Assert.False(ModeLogic.UsesSprite(Mode("arch"), "statue", lists));
    }

    [Fact]
    public void ArchMode_IgnoresTheSpriteList()
    {
        // an object absent from the list is still a sprite in arch mode
        Assert.True(ModeLogic.UsesSprite(Mode("arch"), "beds", Lists(new[] { "clownportal" })));
    }

    [Theory]
    [InlineData("2d")]
    [InlineData("full3d")]
    public void NonHybridModes_AreNotDecidedPerObject(string id)
    {
        var lists = Lists(new[] { "mesh: fridges" }, new[] { "3d trashoutside" });
        Assert.True(ModeLogic.UsesSprite(Mode(id), "fridges", lists));
        Assert.True(ModeLogic.UsesSprite(Mode(id), "trashoutside", lists));
    }

    [Fact]
    public void UnknownObjectNameIsASprite()
    {
        Assert.True(ModeLogic.UsesSprite(Mode("list"), null, Lists()));
    }

    // ---- overrides -----------------------------------------------------------------------------------
    [Theory]
    [InlineData("list")]
    [InlineData("arch")]
    public void Override_BeatsEverythingInHybridModes(string id)
    {
        var lists = Lists(new[] { "clownportal", "mesh: statue" }, new[] { "3d clownportal", "2d statue", "2d doors", "3d fridges" });
        Assert.False(ModeLogic.UsesSprite(Mode(id), "clownportal", lists));
        Assert.True(ModeLogic.UsesSprite(Mode(id), "statue", lists));
        Assert.True(ModeLogic.UsesSprite(Mode(id), "doors", lists));
        Assert.False(ModeLogic.UsesSprite(Mode(id), "fridges", lists));
    }

    [Fact]
    public void Override_CanBeRevertedByWritingTheOppositeChoice()
    {
        var lists = Lists(user: new[] { "2d trashoutside" });
        Assert.True(ModeLogic.UsesSprite(Mode("list"), "trashoutside", lists));
        ModeLogic.ParseOverrides(new[] { "3d trashoutside" }, lists);
        Assert.False(ModeLogic.UsesSprite(Mode("list"), "trashoutside", lists));
    }

    [Fact]
    public void Overrides_RoundTripThroughTheirFile()
    {
        var a = Lists(user: new[] { "2d trashoutside", "3d clownportal" });
        var b = new ModelLists();
        ModeLogic.ParseOverrides(ModeLogic.SerializeOverrides(a.Overrides), b);
        Assert.Equal(a.Overrides.OrderBy(k => k.Key), b.Overrides.OrderBy(k => k.Key));
    }

    // ---- parsing -------------------------------------------------------------------------------------
    [Fact]
    public void ParseList_IgnoresCommentsBlankLinesAndObsoleteDefaultLines()
    {
        var l = Lists(new[] { "# header", "", "  clownportal   # trailing comment", "default mesh", "default sprite", "mesh: statue", "TREES*" });
        Assert.Equal(new[] { "TREES*", "clownportal" }, l.Sprites.OrderBy(x => x, System.StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "statue" }, l.Meshes.ToArray());
    }

    [Fact]
    public void Names_AreCaseInsensitive()
    {
        var lists = Lists(new[] { "ClownPortal" }, new[] { "3d STEREOS" });
        Assert.True(ModeLogic.UsesSprite(Mode("list"), "clownportal", lists));
        Assert.False(ModeLogic.UsesSprite(Mode("list"), "stereos", lists));
    }

    [Fact]
    public void ParseOverrides_IgnoresMalformedLines()
    {
        var l = Lists(user: new[] { "# c", "2d", "4d x", "2d   spaced name ", "3d b" });
        Assert.Equal(new[] { "b", "spaced name" }, l.Overrides.Keys.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void ReparsingReplacesTheOldContents()
    {
        var l = Lists(new[] { "a", "mesh: b" });
        ModeLogic.ParseList(new[] { "c" }, l);
        Assert.Equal(new[] { "c" }, l.Sprites.ToArray());
        Assert.Empty(l.Meshes);
    }

    // ---- the shipped default list ----------------------------------------------------------------------
    [Fact]
    public void ShippedDefaultList_CoversTheMagicObjectsAndAllPlantLikeObjects()
    {
        var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "spritecards.example.txt");
        var lists = new ModelLists();
        ModeLogic.ParseList(System.IO.File.ReadAllLines(path), lists);
        foreach (var n in new[] { "clownportal", "spellbook", "stereos", "trees", "shrubs", "flowersoutdoor", "plants", "plants2", "plantsspellbound", "topiaries", "hedges" })
            Assert.True(ModeLogic.UsesSprite(Mode("list"), n, lists), n + " should use its 2D model by default");
        foreach (var n in new[] { "fridges", "counters", "showerc", "beds" })
            Assert.False(ModeLogic.UsesSprite(Mode("list"), n, lists), n + " should stay a mesh by default");
    }
}
