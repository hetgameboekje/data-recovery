using RecoveryBLL.Exceptions;
using RecoveryBLL.Models;
using RecoveryDAL.Configuration;
using RecoveryDAL.Xml;

namespace RecoveryBLL.Tests;

public class EnvFileTests
{
    [Fact]
    public void Parses_quotes_comments_and_expansion()
    {
        var v = EnvFile.Parse("""
            # comment
            DB_PROVIDER=sqlserver   # trailing comment
            export DB_NAME=Catalog
            DB_PASSWORD='p@ss#word $NOT'
            DB_CONNECTION="Server=(localdb)\MSSQLLocalDB;Database=${DB_NAME};Password=${DB_PASSWORD}"
            MULTI="a\nb"
            EMPTY=
            """);
        Assert.Equal("sqlserver", v["DB_PROVIDER"]);
        Assert.Equal("Catalog", v["DB_NAME"]);
        Assert.Equal("p@ss#word $NOT", v["DB_PASSWORD"]);                        // single quotes: literal
        Assert.Equal(@"Server=(localdb)\MSSQLLocalDB;Database=Catalog;Password=p@ss#word $NOT", v["DB_CONNECTION"]);
        Assert.Equal("a\nb", v["MULTI"]);
        Assert.Equal("", v["EMPTY"]);
    }

    [Theory]
    [InlineData("NOEQUALS")]
    [InlineData("BAD-KEY=x")]
    [InlineData("OPEN=\"unterminated")]
    public void Rejects_malformed_lines(string line) => Assert.Throws<FormatException>(() => EnvFile.Parse(line));

    [Fact]
    public void Loader_resolves_sqlite_path_relative_to_env_file_and_hides_secrets()
    {
        using var dir = new TempDir();
        var env = dir.File(".env", "DB_PROVIDER=sqlite\nDB_CONNECTION=Data Source=data/cat.db\nNET_PASSWORD=secret\n"u8.ToArray());
        var s = EnvSettingsLoader.Load(env);
        Assert.Equal(DatabaseProvider.Sqlite, s.Provider);
        Assert.Contains(Path.Combine(dir.Path, "data", "cat.db"), s.ConnectionString);
        Assert.Equal("secret", s.Network.Password);
        Assert.DoesNotContain("secret", s.SafeConnectionDescription);
    }

    [Fact]
    public void Sql_server_description_never_contains_the_password()
    {
        var d = EnvSettingsLoader.Describe(DatabaseProvider.SqlServer, "Server=srv;Database=db;User ID=rt;Password=Hunter2");
        Assert.DoesNotContain("Hunter2", d);
        Assert.Contains("user rt", d);
    }

    [Fact]
    public void Unknown_provider_is_a_configuration_error()
    {
        using var dir = new TempDir();
        var env = dir.File(".env", "DB_PROVIDER=oracle\n"u8.ToArray());
        Assert.Throws<ConfigurationException>(() => EnvSettingsLoader.Load(env));
    }
}

public class XmlDictionaryTests
{
    [Fact]
    public void Loads_the_shipped_dictionaries_with_the_dom()
    {
        var d = new XmlDictionaryRepository(TestHost.DictionariesFile).Load();
        Assert.Equal("Photos", d.CategoryOf("jpg"));
        Assert.Equal("Videos", d.CategoryOf("MP4"));
        Assert.Equal("Other", d.CategoryOf("xyz"));
        Assert.Contains("System Volume Information", d.SkipFolders);
        Assert.Contains("rtpart", d.PartialFileExtensions);
        Assert.Equal(8, d.BusTypes[BusType.Nvme].ReadThreads);
        Assert.True(d.BusTypes[BusType.Nvme].TrimWarning);
        Assert.Equal("USB", d.BusTypes[BusType.Usb].Name);
        Assert.Contains((64, 64), d.Junk.SkinSizes);
        Assert.Equal(640, d.Junk.PreviewMaxLongSide);
    }

    [Fact]
    public void Same_extension_in_two_categories_is_rejected()
    {
        using var dir = new TempDir();
        var f = dir.File("d.xml", """
            <dictionaries>
              <dictionary name="categories"><category name="A"><extension>x</extension></category><category name="B"><extension>x</extension></category></dictionary>
            </dictionaries>
            """u8.ToArray());
        var ex = Assert.Throws<ConfigurationException>(() => new XmlDictionaryRepository(f).Load());
        Assert.Contains("'x'", ex.Message);
    }

    [Fact]
    public void Dtd_and_external_entities_are_refused()
    {
        using var dir = new TempDir();
        var f = dir.File("d.xml", """
            <?xml version="1.0"?>
            <!DOCTYPE d [ <!ENTITY x SYSTEM "file:///c:/windows/win.ini"> ]>
            <dictionaries>&x;</dictionaries>
            """u8.ToArray());
        Assert.Throws<ConfigurationException>(() => new XmlDictionaryRepository(f).Load());
    }
}
