using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

// mdump <dll> <keywords csv> [out.txt]
// 枚举类型/字段/方法名命中关键词的托管程序集元数据（只读结构，不加载运行时）。
var dll = args[0];
var kws = args[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
var outPath = args.Length > 2 ? args[2] : null;
var sb = new StringBuilder();

using var fs = File.OpenRead(dll);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();

bool Hit(string s)
{
    foreach (var k in kws) if (s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
    return false;
}

int nTypes = 0;
foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    var tn = md.GetString(td.Name);
    var ns = md.GetString(td.Namespace);
    var full = string.IsNullOrEmpty(ns) ? tn : ns + "." + tn;
    var matched = Hit(tn);
    var fields = new List<string>();
    foreach (var fh in td.GetFields())
    {
        var fd = md.GetFieldDefinition(fh);
        var fn = md.GetString(fd.Name);
        if (Hit(fn)) { matched = true; }
        string ftype = "?";
        try
        {
            var d = fd.DecodeSignature(new SimpleProvider(), genericContext: default);
            ftype = d;
        }
        catch { }
        fields.Add($"    field {fn} : {ftype}");
    }
    if (!matched) continue;
    nTypes++;
    sb.AppendLine($"TYPE {full}");
    foreach (var f in fields) sb.AppendLine(f);
    foreach (var mh in td.GetMethods())
    {
        var mo = md.GetMethodDefinition(mh);
        var mn = md.GetString(mo.Name);
        string sig = "";
        try { var ms = mo.DecodeSignature(new SimpleProvider(), default); sig = ms.ReturnType + " (" + string.Join(",", ms.ParameterTypes) + ")"; } catch { }
        sb.AppendLine($"    method {mn} : {sig}");
    }
}
var result = sb.ToString();
Console.WriteLine($"[mdump] {dll}: matched {nTypes} types for [{string.Join(',', kws)}], {result.Length} chars");
if (outPath != null) File.WriteAllText(outPath, result, new UTF8Encoding(false));

// 简易签名提供者：只还原类型名
class SimpleProvider : ISignatureTypeProvider<string, object>
{
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte raw)
    { var t = r.GetTypeDefinition(h); var n = r.GetString(t.Name); var ns = r.GetString(t.Namespace); return string.IsNullOrEmpty(ns) ? n : ns + "." + n; }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte raw)
    { var t = r.GetTypeReference(h); var n = r.GetString(t.Name); var ns = r.GetString(t.Namespace); return string.IsNullOrEmpty(ns) ? n : ns + "." + n; }
    public string GetSZArrayType(string e) => e + "[]";
    public string GetArrayType(string e, ArrayShape s) => e + "[...]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetPointerType(string e) => e + "*";
    public string GetGenericInstantiation(string g, System.Collections.Immutable.ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object o, int i) => "!!" + i;
    public string GetGenericTypeParameter(object o, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool req) => u;
    public string GetPinnedType(string e) => e;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetTypeFromSpecification(MetadataReader r, object o, TypeSpecificationHandle h, byte raw) => "spec";
}
