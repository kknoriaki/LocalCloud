using System.Diagnostics;
using LocalCloud.Core;
using LocalCloud.Infrastructure;
using Microsoft.Data.Sqlite;
var root = Path.Combine(Path.GetTempPath(), "LocalCloud-benchmark-" + Guid.NewGuid().ToString("N"));
try
{
    var db = new Database(new SafePaths(root)); var clock = Stopwatch.StartNew();
    using (var c = db.Open()) using (var tx = c.BeginTransaction()) using (var cmd = Database.Command(c, "INSERT INTO Files(id,path,name,extension,mime,size,kind,modifiedAt,importedAt) VALUES($id,$path,$name,'jpg','image/jpeg',4000000,'photo','2026-10-05T00:00:00Z','2026-10-05T00:00:00Z')", ("$id", ""), ("$path", ""), ("$name", ""))) { cmd.Transaction = tx; cmd.Prepare(); for (var i = 0; i < 100000; i++) { var name = $"Фото{i:D6}.jpg"; cmd.Parameters["$id"].Value = i.ToString(); cmd.Parameters["$path"].Value = "Photos/Тест/" + name; cmd.Parameters["$name"].Value = name; cmd.ExecuteNonQuery(); } tx.Commit(); }
    Console.WriteLine($"100,000 metadata entries inserted: {clock.ElapsedMilliseconds} ms"); clock.Restart(); var page = db.Query(new()); if (page.Total != 100000 || page.Items.Count != 120) throw new Exception("pagination wrong"); Console.WriteLine($"First page (120 / 100,000): {clock.ElapsedMilliseconds} ms"); clock.Restart(); var last = db.Query(new(Offset: 99840)); Console.WriteLine($"Last page: {clock.ElapsedMilliseconds} ms"); clock.Restart(); var search = db.Query(new(Search: "ФОТО099")); if (search.Total != 1000) throw new Exception("search wrong"); Console.WriteLine($"Cyrillic substring search, 1000 matches: {clock.ElapsedMilliseconds} ms");
    Console.WriteLine("PASS: 100k SQLite paging and search. This is a metadata benchmark, not a real 100k-media ingestion benchmark.");
}
finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
