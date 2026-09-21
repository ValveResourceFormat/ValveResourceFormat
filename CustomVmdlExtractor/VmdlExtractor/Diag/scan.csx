using System;
using System.Linq;
using SteamDatabase.ValvePak;
using var pkg = new Package();
pkg.Read(@"D:\Games\Dota 2 7.28c\game\dota\pak01_dir.vpk");
var entries = pkg.Entries;
foreach (var (ext, list) in entries) {
  if (ext.Contains("agrp") || ext.Contains("vagrp")) {
    Console.WriteLine($"ext: {ext}");
  }
}
foreach (var (ext, list) in entries) {
  if (ext.Contains("agrp") || ext.Contains("vagrp")) {
    foreach (var e in list.Where(e => e.GetFullPath().Contains("shadow_fiend"))) {
      Console.WriteLine($"  {e.GetFullPath()}");
    }
  }
}
