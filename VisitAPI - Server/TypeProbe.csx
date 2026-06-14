#r "D:\EFT\SPT\SPTarkov.Server.Core.dll"
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
var t = typeof(VisibilityCondition);
foreach (var p in t.GetProperties())
    Console.WriteLine($"Property: {p.Name} : {p.PropertyType.Name}");
foreach (var f in t.GetFields())
    Console.WriteLine($"Field: {f.Name} : {f.FieldType.Name}");
