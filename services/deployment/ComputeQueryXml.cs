using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SaveIT.Deployment;

internal static class ComputeQueryXml
{
    internal static readonly XNamespace Namespace="http://ec2.amazonaws.com/doc/2016-11-15/";
    internal static XElement Read(byte[] bytes,string action)
    {
        _=new UTF8Encoding(false,true).GetCharCount(bytes);
        using var input=new MemoryStream(bytes,false);
        var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1048576,CheckCharacters=true};
        using(var reader=XmlReader.Create(input,settings)) {var nodes=0;while(reader.Read())if(reader.Depth>24||++nodes>32768)throw new InvalidOperationException();}
        input.Position=0;using var again=XmlReader.Create(input,settings);var document=XDocument.Load(again);var root=document.Root??throw new InvalidOperationException();
        if(root.Name!=Namespace+(action+"Response")||document.DescendantNodes().Any(n=>n is XComment or XProcessingInstruction or XCData)||
            root.DescendantsAndSelf().Any(e=>e.Name.Namespace!=Namespace||e.Attributes().Any(a=>!a.IsNamespaceDeclaration)))throw new InvalidOperationException();
        return root;
    }
    internal static Dictionary<string,XElement> Fields(XElement element,string[] required,params string[] optional)
    {
        if(element.Nodes().OfType<XText>().Any(x=>!string.IsNullOrWhiteSpace(x.Value)))throw new InvalidOperationException();
        var result=new Dictionary<string,XElement>(StringComparer.Ordinal);
        foreach(var child in element.Elements())if(child.Name.Namespace!=Namespace||(!required.Contains(child.Name.LocalName,StringComparer.Ordinal)&&!optional.Contains(child.Name.LocalName,StringComparer.Ordinal))||!result.TryAdd(child.Name.LocalName,child))throw new InvalidOperationException();
        if(required.Any(k=>!result.ContainsKey(k)))throw new InvalidOperationException();return result;
    }
    internal static string Text(XElement element,int minimum=0,int maximum=1024)
    {
        var text=element.Value;if(element.HasElements||text.Length<minimum||text.Length>maximum||text.Any(c=>c<32||c==127))throw new InvalidOperationException();return text;
    }
    internal static XElement[] Items(XElement element,int maximum=1000)
    {
        if(element.Nodes().OfType<XText>().Any(x=>!string.IsNullOrWhiteSpace(x.Value)))throw new InvalidOperationException();
        var items=element.Elements().Take(maximum+1).ToArray();if(items.Length>maximum||items.Any(x=>x.Name!=Namespace+"item"))throw new InvalidOperationException();return items;
    }
    internal static XElement[] Inventory(XElement response,string set,int count)
    {
        var root=Fields(response,["requestId",set],"nextToken");_=Text(root["requestId"],1,128);
        if(root.TryGetValue("nextToken",out var next)&&Text(next)!="")throw new InvalidOperationException();
        var items=Items(root[set],count);if(items.Length!=count)throw new InvalidOperationException();return items;
    }
    internal static Dictionary<string,string> ReadTags(XElement? element)
    {
        var tags=new Dictionary<string,string>(StringComparer.Ordinal);
        if(element is not null)foreach(var item in Items(element,50)) {
            var fields=Fields(item,["key","value"]);
            if(!tags.TryAdd(Text(fields["key"],1,128),Text(fields["value"],0,256)))throw new InvalidOperationException();
        }
        return tags;
    }
    internal static void RequireScopeTags(Dictionary<string,string> tags,ComputeScope scope)
    {
        if(tags.GetValueOrDefault("Project")!="SaveIT"||tags.GetValueOrDefault("Cell")!=scope.Cell||tags.GetValueOrDefault("Environment")!=scope.Environment)throw new InvalidOperationException();
    }
}
