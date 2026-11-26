/*
    Kiwi's Co-Op Mod for Half-Life: Alyx
    Copyright (c) 2022 KiwifruitDev
    All rights reserved.
    This software is licensed under the MIT License.
    -----------------------------------------------------------------------------
    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
    -----------------------------------------------------------------------------
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KiwisCoOpModCore
{
    // Finds Half-Life: Alyx through Steam's libraryfolders.vdf and copies the kiwimp_alyx addon into
    // its game/hlvr_addons folder, now that the Workshop item is gone (UPGRADE_PLAN.md W9).
    // tools/install-addon.ps1 does the same outside the app.
    public static class AddonInstaller
    {
        public const string AppId = "546560";
        public const string AddonName = "kiwimp_alyx";
        public const string DefaultInstallDir = "Half-Life Alyx";

        // Steam library folders listed in libraryfolders.vdf: the "path" of each entry, or, in the
        // older format, the value of each numbered key.
        public static List<string> ParseLibraryFolders(string vdf)
        {
            List<string> folders = new();
            int depth = 0;
            string? key = null;
            foreach (string? token in Tokenize(vdf))
            {
                if (token == null)
                    continue;
                if (token == "{")
                {
                    depth++;
                    key = null;
                }
                else if (token == "}")
                {
                    depth--;
                    key = null;
                }
                else if (key == null)
                    key = token;
                else
                {
                    if ((depth == 2 && key.Equals("path", StringComparison.OrdinalIgnoreCase)) || (depth == 1 && key.All(char.IsDigit)))
                        folders.Add(token);
                    key = null;
                }
            }
            return folders;
        }

        // The value of a top-level key of an appmanifest_<id>.acf file, such as "installdir".
        public static string? ParseManifestValue(string acf, string name)
        {
            string? key = null;
            int depth = 0;
            foreach (string? token in Tokenize(acf))
            {
                if (token == null)
                    continue;
                if (token == "{" || token == "}")
                {
                    depth += token == "{" ? 1 : -1;
                    key = null;
                }
                else if (key == null)
                    key = token;
                else
                {
                    if (depth == 1 && key.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return token;
                    key = null;
                }
            }
            return null;
        }

        // Quoted strings come back unescaped, braces as "{" and "}". Unquoted words are skipped (null).
        private static IEnumerable<string?> Tokenize(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                    i++;
                else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                        i++;
                }
                else if (c == '{' || c == '}')
                {
                    yield return c.ToString();
                    i++;
                }
                else if (c == '"')
                {
                    StringBuilder builder = new();
                    i++;
                    while (i < text.Length && text[i] != '"')
                    {
                        if (text[i] == '\\' && i + 1 < text.Length)
                            i++;
                        builder.Append(text[i]);
                        i++;
                    }
                    i++;
                    yield return builder.ToString();
                }
                else
                {
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '"' && text[i] != '{' && text[i] != '}')
                        i++;
                    yield return null;
                }
            }
        }

        // The Half-Life Alyx folder (the one holding game/hlvr) in the first Steam library that has it.
        public static string? FindGameDirectory(IEnumerable<string> steamDirectories)
        {
            foreach (string steam in steamDirectories)
            {
                List<string> libraries = new() { steam };
                string vdfPath = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdfPath))
                    libraries.AddRange(ParseLibraryFolders(File.ReadAllText(vdfPath)));
                foreach (string library in libraries)
                {
                    string steamapps = Path.Combine(library, "steamapps");
                    string manifest = Path.Combine(steamapps, "appmanifest_" + AppId + ".acf");
                    string installDir = DefaultInstallDir;
                    if (File.Exists(manifest))
                        installDir = ParseManifestValue(File.ReadAllText(manifest), "installdir") ?? DefaultInstallDir;
                    string game = Path.Combine(steamapps, "common", installDir);
                    if (IsGameDirectory(game))
                        return game;
                }
            }
            return null;
        }

        public static bool IsGameDirectory(string directory) => Directory.Exists(Path.Combine(directory, "game", "hlvr"));

        public static string GetTargetDirectory(string gameDirectory) => Path.Combine(gameDirectory, "game", "hlvr_addons", AddonName);

        // Replaces game/hlvr_addons/kiwimp_alyx with a copy of addonSource and returns its path. An
        // addon folder linked to a clone (README "Symlinks") is left alone: copying would overwrite
        // the clone with itself.
        public static string Install(string addonSource, string gameDirectory)
        {
            if (!File.Exists(Path.Combine(addonSource, "addoninfo.txt")))
                throw new DirectoryNotFoundException("No addon found in " + addonSource);
            if (!IsGameDirectory(gameDirectory))
                throw new DirectoryNotFoundException(gameDirectory + " is not a Half-Life: Alyx folder (it has no game\\hlvr folder)");
            string target = GetTargetDirectory(gameDirectory);
            if (Directory.Exists(target))
            {
                if (ContainsLink(target))
                    throw new InvalidOperationException(target + " is linked to another folder (a development setup), so it was left as is");
                Directory.Delete(target, true);
            }
            CopyDirectory(addonSource, target);
            return target;
        }

        private static bool ContainsLink(string directory)
        {
            if (new DirectoryInfo(directory).LinkTarget != null)
                return true;
            return new DirectoryInfo(directory).EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Any(info => info.LinkTarget != null);
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}
