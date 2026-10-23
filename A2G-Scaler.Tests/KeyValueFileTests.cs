using System;
using System.IO;
using System.Text;
using Xunit;

namespace A2GScaler.Tests
{
    public class KeyValueFileTests : TempFolderTest
    {
        string PathOf(string name) { return Path.Combine(KeyValueFile.Folder, name); }

        [Fact]
        public void Folder_UsesOverrideAndCreatesIt()
        {
            Assert.Equal(Folder, KeyValueFile.Folder);
            Assert.True(Directory.Exists(Folder));
        }

        [Fact]
        public void Folder_DefaultsToLocalAppData()
        {
            KeyValueFile.FolderOverride = null;
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "A2G-Scaler");
            Assert.Equal(expected, KeyValueFile.Folder);
        }

        [Fact]
        public void Load_MissingFile_IsEmpty()
        {
            var f = new KeyValueFile(PathOf("none.ini"));
            Assert.False(f.Exists);
            Assert.Same(f, f.Load());
            Assert.False(f.Has("x"));
            Assert.Equal("fb", f.Get("x", "fb"));
        }

        [Fact]
        public void SaveAndLoad_RoundTripsAllTypes()
        {
            var f = new KeyValueFile(PathOf("a.ini"));
            f.Set("s", "Hallo Welt");
            f.Set("i", -42);
            f.Set("l", 1L << 40);
            f.Set("t", true);
            f.Set("b", false);
            f.Save();
            Assert.True(f.Exists);

            var g = new KeyValueFile(PathOf("a.ini")).Load();
            Assert.Equal("Hallo Welt", g.Get("s", null));
            Assert.Equal(-42, g.GetInt("i", 0));
            Assert.Equal(1L << 40, g.GetLong("l", 0));
            Assert.True(g.GetBool("t", false));
            Assert.False(g.GetBool("b", true));
        }

        [Fact]
        public void Getters_FallBackOnMissingOrInvalidValues()
        {
            var f = new KeyValueFile(PathOf("b.ini"));
            f.Set("n", "abc");
            Assert.Equal(7, f.GetInt("n", 7));
            Assert.Equal(7L, f.GetLong("n", 7));
            Assert.True(f.GetBool("n", true));
            Assert.False(f.GetBool("missing", false));
            Assert.Equal(3, f.GetInt("missing", 3));
        }

        [Fact]
        public void Load_IgnoresCommentsAndLinesWithoutKey_TrimsAndStripsBom()
        {
            File.WriteAllText(PathOf("c.ini"), "﻿first=1\n# comment=2\n=nokey\nnoequals\n  spaced  =  value  \nKEY=upper\n", Encoding.UTF8);
            var f = new KeyValueFile(PathOf("c.ini")).Load();
            Assert.Equal("1", f.Get("first", null));
            Assert.False(f.Has("# comment"));
            Assert.Equal("value", f.Get("spaced", null));
            Assert.Equal("upper", f.Get("key", null)); // keys are case-insensitive
        }

        [Fact]
        public void Load_ReplacesPreviousValues()
        {
            var f = new KeyValueFile(PathOf("d.ini"));
            f.Set("only-in-memory", 1);
            f.Load();
            Assert.False(f.Has("only-in-memory"));
        }

        [Fact]
        public void RemoveAndDelete()
        {
            var f = new KeyValueFile(PathOf("e.ini"));
            f.Set("a", 1); f.Set("b", 2);
            f.Remove("a");
            Assert.False(f.Has("a"));
            f.Save();
            f.Delete();
            Assert.False(f.Exists);
            Assert.False(f.Has("b"));
            f.Delete(); // deleting a missing file is fine
        }
    }
}
