using System;
using System.IO;
using KiwisCoOpModCore;
using Xunit;

namespace KiwisCoOpModCore.Tests
{
    // Steam's file formats and the copy into game/hlvr_addons (UPGRADE_PLAN.md W9).
    public sealed class AddonInstallerTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "kcom-addon-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private const string LibraryFolders = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"label"		""
            		"apps"
            		{
            			"228980"		"406733323"
            		}
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            		"apps"
            		{
            			"546560"		"74245290812"
            		}
            	}
            }
            """;

        [Fact]
        public void ParseLibraryFolders_ReadsEachPathUnescaped()
        {
            Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, AddonInstaller.ParseLibraryFolders(LibraryFolders));
        }

        [Fact]
        public void ParseLibraryFolders_ReadsTheOldFormat()
        {
            string vdf = """
                "LibraryFolders"
                {
                	"TimeNextStatsReport"		"1612345678"
                	"ContentStatsID"		"-123"
                	"1"		"E:\\Games\\Steam"
                }
                """;

            Assert.Equal(new[] { @"E:\Games\Steam" }, AddonInstaller.ParseLibraryFolders(vdf));
        }

        [Fact]
        public void ParseLibraryFolders_UnescapesQuotes()
        {
            Assert.Equal(new[] { "/mnt/Library \"2\"" }, AddonInstaller.ParseLibraryFolders("\"libraryfolders\" { \"0\" { \"path\" \"/mnt/Library \\\"2\\\"\" } }"));
        }

        [Fact]
        public void ParseManifestValue_ReadsTopLevelKeyOnly()
        {
            string acf = """
                "AppState"
                {
                	"appid"		"546560"
                	"installdir"		"Half-Life Alyx"
                	"UserConfig"
                	{
                		"installdir"		"wrong"
                	}
                }
                """;

            Assert.Equal("Half-Life Alyx", AddonInstaller.ParseManifestValue(acf, "installdir"));
            Assert.Null(AddonInstaller.ParseManifestValue(acf, "missing"));
        }

        [Fact]
        public void FindGameDirectory_SearchesEveryLibrary()
        {
            string steam = Path.Combine(root, "Steam");
            string library = Path.Combine(root, "Library 2");
            Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
            string vdf = "\"libraryfolders\" { \"0\" { \"path\" \"" + steam.Replace("\\", "\\\\") + "\" } \"1\" { \"path\" \"" + library.Replace("\\", "\\\\") + "\" } }";
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), vdf);
            string game = Path.Combine(library, "steamapps", "common", "HLA");
            Directory.CreateDirectory(Path.Combine(game, "game", "hlvr"));
            File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_546560.acf"), "\"AppState\" { \"installdir\" \"HLA\" }");

            Assert.Equal(game, AddonInstaller.FindGameDirectory(new[] { Path.Combine(root, "missing"), steam }));
        }

        [Fact]
        public void FindGameDirectory_ReturnsNullWithoutTheGame()
        {
            Directory.CreateDirectory(Path.Combine(root, "Steam", "steamapps", "common", "Half-Life Alyx"));

            Assert.Null(AddonInstaller.FindGameDirectory(new[] { Path.Combine(root, "Steam") }));
        }

        private string MakeAddon()
        {
            string source = Path.Combine(root, "addon");
            Directory.CreateDirectory(Path.Combine(source, "scripts", "vscripts"));
            File.WriteAllText(Path.Combine(source, "addoninfo.txt"), "info");
            File.WriteAllText(Path.Combine(source, "scripts", "vscripts", "kcom_interval.lua"), "-- new");
            return source;
        }

        private string MakeGame()
        {
            string game = Path.Combine(root, "Half-Life Alyx");
            Directory.CreateDirectory(Path.Combine(game, "game", "hlvr"));
            return game;
        }

        [Fact]
        public void Install_CopiesTheAddonAndReplacesAnOldCopy()
        {
            string source = MakeAddon();
            string game = MakeGame();
            string old = Path.Combine(game, "game", "hlvr_addons", "kiwimp_alyx", "scripts", "vscripts");
            Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(old, "kcom_interval.lua"), "-- old");
            File.WriteAllText(Path.Combine(old, "removed.lua"), "-- gone upstream");

            string target = AddonInstaller.Install(source, game);

            Assert.Equal(Path.Combine(game, "game", "hlvr_addons", "kiwimp_alyx"), target);
            Assert.Equal("info", File.ReadAllText(Path.Combine(target, "addoninfo.txt")));
            Assert.Equal("-- new", File.ReadAllText(Path.Combine(target, "scripts", "vscripts", "kcom_interval.lua")));
            Assert.False(File.Exists(Path.Combine(target, "scripts", "vscripts", "removed.lua")));
        }

        [Fact]
        public void Install_LeavesALinkedAddonAlone()
        {
            string source = MakeAddon();
            string game = MakeGame();
            string target = Path.Combine(game, "game", "hlvr_addons", "kiwimp_alyx");
            Directory.CreateDirectory(target);
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(target, "scripts"), Path.Combine(source, "scripts"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Windows only lets admins, or Developer Mode, create symbolic links.
                Assert.Skip("Can't create a symbolic link here: " + e.Message);
            }

            Assert.Throws<InvalidOperationException>(() => AddonInstaller.Install(source, game));
            Assert.Equal("-- new", File.ReadAllText(Path.Combine(source, "scripts", "vscripts", "kcom_interval.lua")));
        }

        [Fact]
        public void Install_RefusesAFolderThatIsNotTheGame()
        {
            string source = MakeAddon();
            Directory.CreateDirectory(Path.Combine(root, "Documents"));

            Assert.Throws<DirectoryNotFoundException>(() => AddonInstaller.Install(source, Path.Combine(root, "Documents")));
            Assert.Throws<DirectoryNotFoundException>(() => AddonInstaller.Install(Path.Combine(root, "Documents"), MakeGame()));
        }
    }
}
