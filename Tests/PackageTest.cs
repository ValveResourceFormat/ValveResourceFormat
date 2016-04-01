using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using ValveResourceFormat;

namespace Tests
{
    [TestFixture]
    public class PackageTest
    {
        [Test]
        public void ParseVPK()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "VPK", "platform_misc_dir.vpk");

            using (var package = new Package())
            {
                package.Read(path);

                package.VerifyHashes();

                Assert.IsTrue(package.IsSignatureValid());
            }
        }

        [Test]
        public void InvalidPackageThrows()
        {
            using (var resource = new Package())
            {
                using (var ms = new MemoryStream(Enumerable.Repeat<byte>(1, 12).ToArray()))
                {
                    // Should yell about not setting file name
                    Assert.Throws<InvalidOperationException>(() => resource.Read(ms));

                    resource.SetFileName("a.vpk");

                    Assert.Throws<InvalidDataException>(() => resource.Read(ms));
                }
            }
        }

        [Test]
        public void CorrectHeaderWrongVersionThrows()
        {
            using (var resource = new Package())
            {
                resource.SetFileName("a.vpk");

                using (var ms = new MemoryStream(new byte[] { 0x34, 0x12, 0xAA, 0x55, 0x11, 0x11, 0x11, 0x11, 0x22, 0x22, 0x22, 0x22 }))
                {
                    Assert.Throws<InvalidDataException>(() => resource.Read(ms));
                }
            }
        }

        [Test]
        public void FindEntryDeep()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "VPK", "platform_misc_dir.vpk");

            using (var package = new Package())
            {
                package.Read(path);

                Assert.AreEqual(0xA4115395, package.FindEntry("addons\\chess\\chess.vdf")?.CRC32);
                Assert.AreEqual(0xA4115395, package.FindEntry("addons\\chess\\", "chess.vdf")?.CRC32);
                Assert.AreEqual(0xA4115395, package.FindEntry("addons\\chess\\", "chess", "vdf")?.CRC32);

                Assert.AreEqual(0xA4115395, package.FindEntry("addons/chess\\chess.vdf")?.CRC32);
                Assert.AreEqual(0xA4115395, package.FindEntry("addons/chess\\", "chess.vdf")?.CRC32);
                Assert.AreEqual(0xA4115395, package.FindEntry("addons/chess\\", "chess", "vdf")?.CRC32);

                Assert.AreEqual(0xA4115395, package.FindEntry("addons/chess/chess.vdf")?.CRC32);
                Assert.AreEqual(0xA4115395, package.FindEntry("addons/chess/", "chess.vdf")?.CRC32);
                Assert.AreEqual(0xA4115395, package.FindEntry("addons/chess/", "chess", "vdf")?.CRC32);

                Assert.AreEqual(0xA4115395, package.FindEntry("\\addons/chess/chess.vdf")?.CRC32);
                Assert.AreEqual(0xA4115395, package.FindEntry("\\addons/chess/", "chess.vdf")?.CRC32);
                Assert.AreEqual(0xA4115395, package.FindEntry("\\addons/chess/", "chess", "vdf")?.CRC32);

                Assert.AreEqual(0xA4115395, package.FindEntry("/addons/chess/chess.vdf")?.CRC32);
                Assert.AreEqual(0xA4115395, package.FindEntry("/addons/chess/", "chess.vdf")?.CRC32);
                Assert.AreEqual(0xA4115395, package.FindEntry("/addons/chess/", "chess", "vdf")?.CRC32);

                Assert.IsNull(package.FindEntry("\\addons/chess/hello_github_reader.vdf"));
                Assert.IsNull(package.FindEntry("\\addons/chess/", "hello_github_reader.vdf"));
                Assert.IsNull(package.FindEntry("\\addons/chess/", "hello_github_reader", "vdf"));

                Assert.IsNull(package.FindEntry("\\addons/hello_github_reader/chess.vdf"));
                Assert.IsNull(package.FindEntry("\\addons/hello_github_reader/", "chess.vdf"));
                Assert.IsNull(package.FindEntry("\\addons/hello_github_reader/", "chess", "vdf"));

                Assert.IsNull(package.FindEntry("\\addons/", "chess/chess.vdf"));
                Assert.IsNull(package.FindEntry("\\addons/", "chess/chess", "vdf"));
                Assert.IsNull(package.FindEntry("\\addons/", "chess\\chess.vdf"));
                Assert.IsNull(package.FindEntry("\\addons/", "chess\\chess", "vdf"));
            }
        }

        [Test]
        public void FindEntryRoot()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "VPK", "steamdb_test_single.vpk");

            using (var package = new Package())
            {
                package.Read(path);

                Assert.AreEqual(0x9C800116, package.FindEntry("kitten.jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry("", "kitten.jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry("", "kitten", "jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry(null, "kitten.jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry(null, "kitten", "jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry(null, "/kitten.jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry(null, "\\kitten.jpg")?.CRC32);

                Assert.AreEqual(0x9C800116, package.FindEntry("\\kitten.jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry("\\", "kitten.jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry("\\", "kitten", "jpg")?.CRC32);

                Assert.AreEqual(0x9C800116, package.FindEntry("/kitten.jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry("/", "kitten.jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry("/", "kitten", "jpg")?.CRC32);

                Assert.AreEqual(0x9C800116, package.FindEntry("\\/kitten.jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry("\\/\\", "kitten.jpg")?.CRC32);
                Assert.AreEqual(0x9C800116, package.FindEntry("\\\\/", "kitten", "jpg")?.CRC32);

                Assert.IsNull(package.FindEntry(null));
                Assert.IsNull(package.FindEntry(null, null));
                Assert.IsNull(package.FindEntry(null, null, null));

            }
        }

        [Test]
        public void ExtractInlineVPK()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "VPK", "steamdb_test_single.vpk");

            TestVPKExtraction(path);
        }

        [Test]
        public void ExtractDirVPK()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "VPK", "steamdb_test_dir.vpk");

            TestVPKExtraction(path);
        }

        private void TestVPKExtraction(string path)
        {
            using (var package = new Package())
            {
                package.Read(path);

                Assert.AreEqual(2, package.Entries.Count);
                Assert.Contains("jpg", package.Entries.Keys);
                Assert.Contains("proto", package.Entries.Keys);

                var flatEntries = new Dictionary<string, PackageEntry>();

                using (var sha1 = new SHA1CryptoServiceProvider())
                {
                    var data = new Dictionary<string, string>();

                    foreach (var a in package.Entries)
                    {
                        foreach (var b in a.Value)
                        {
                            Assert.AreEqual(a.Key, b.TypeName);

                            flatEntries.Add(b.FileName, b);

                            byte[] entry;
                            package.ReadEntry(b, out entry);

                            data.Add(b.FileName + '.' + b.TypeName, BitConverter.ToString(sha1.ComputeHash(entry)).Replace("-", ""));
                        }
                    }

                    Assert.AreEqual(3, data.Count);
                    Assert.AreEqual("E0D865F19F0A4A7EA3753FBFCFC624EE8B46928A", data["kitten.jpg"]);
                    Assert.AreEqual("2EFFCB09BE81E8BEE88CB7BA8C18E87D3E1168DB", data["steammessages_base.proto"]);
                    Assert.AreEqual("22741F66442A4DC880725D2CC019E6C9202FD70C", data["steammessages_clientserver.proto"]);
                }

                Assert.AreEqual(flatEntries["kitten"].TotalLength, 16361);
                Assert.AreEqual(flatEntries["steammessages_base"].TotalLength, 2563);
                Assert.AreEqual(flatEntries["steammessages_clientserver"].TotalLength, 39177);
            }
        }
    }
}
