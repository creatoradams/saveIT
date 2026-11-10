using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

// Exactly one SDK-signed read. Original response semantics survive SDK model deserialization.
internal sealed class ComputeAwsTransport(HttpMessageHandler inner,ComputeScope scope,string? taskArn,CancellationToken operation):DelegatingHandler(inner)
{
    private int attempts;
    private byte[]? retained;
    internal JsonElement TaskResponse { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        var service=taskArn is null?"sts":"ecs";
        if(Interlocked.Increment(ref attempts)!=1 || request.Method!=HttpMethod.Post || request.RequestUri?.AbsoluteUri!=$"https://{service}.{scope.Region}.amazonaws.com/" || request.Content is null || request.Content.Headers.ContentEncoding.Count!=0)throw new InvalidOperationException();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(operation,ct);deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var requestBytes=await Bounded(request.Content,1024,deadline.Token);
        try
        {
            var text=new UTF8Encoding(false,true).GetString(requestBytes);
            if(taskArn is null)
            {
                if(text is not ("Action=GetCallerIdentity&Version=2011-06-15" or "Version=2011-06-15&Action=GetCallerIdentity")||request.Headers.Contains("X-Amz-Target"))throw new InvalidOperationException();
            }
            else
            {
                if(!request.Headers.TryGetValues("X-Amz-Target",out var targets)||targets.Single()!="AmazonEC2ContainerServiceV20141113.DescribeTaskDefinition")throw new InvalidOperationException();
                using var json=JsonDocument.Parse(requestBytes,new JsonDocumentOptions{MaxDepth=4});RejectDuplicates(json.RootElement);
                var body=Object(json.RootElement,"taskDefinition","include");
                if(body["taskDefinition"].GetString()!=taskArn || body["include"].ValueKind!=JsonValueKind.Array || body["include"].GetArrayLength()!=1 || body["include"][0].GetString()!="TAGS")throw new InvalidOperationException();
            }
            var replacement=new ByteArrayContent(requestBytes);foreach(var header in request.Content.Headers)replacement.Headers.TryAddWithoutValidation(header.Key,header.Value);
            request.Content.Dispose();request.Content=replacement;
            request.Version=HttpVersion.Version11;request.VersionPolicy=HttpVersionPolicy.RequestVersionExact;request.Headers.ConnectionClose=true;
            HttpResponseMessage? response=null;
            try
            {
                response=await base.SendAsync(request,deadline.Token);
                if(response.StatusCode!=HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count!=0 || response.Headers.Contains("Trailer"))throw new InvalidOperationException();
                var bytes=await Bounded(response.Content,taskArn is null?16384:65536,deadline.Token);
                try
                {
                    _=new UTF8Encoding(false,true).GetCharCount(bytes);
                    if(taskArn is null)ValidateIdentityXml(bytes);
                    else {
                        using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=24});var root=document.RootElement;RejectDuplicates(root);
                        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("taskDefinition",out _)||root.EnumerateObject().Any(x=>x.Name is not("taskDefinition" or "tags")))throw new InvalidOperationException();
                        TaskResponse=root.Clone();
                    }
                    if(response.TrailingHeaders.Any())throw new InvalidOperationException();
                    var content=new ByteArrayContent(bytes);foreach(var header in response.Content.Headers)content.Headers.TryAddWithoutValidation(header.Key,header.Value);
                    response.Content.Dispose();response.Content=content;retained=bytes;bytes=[];
                    deadline.Token.ThrowIfCancellationRequested();var result=response;response=null;return result;
                }
                finally{CryptographicOperations.ZeroMemory(bytes);}
            }
            finally{response?.Dispose();}
        }
        finally{CryptographicOperations.ZeroMemory(requestBytes);}
    }
    private static async Task<byte[]> Bounded(HttpContent content,int limit,CancellationToken ct)
    {
        if(content.Headers.ContentLength>limit)throw new InvalidOperationException();
        var buffer=new byte[limit+1];
        try
        {
            using var stream=await content.ReadAsStreamAsync(ct);var count=0;
            while(count<buffer.Length){var read=await stream.ReadAsync(buffer.AsMemory(count),ct);if(read==0)break;count+=read;}
            if(count is <1||count>limit||content.Headers.ContentLength is long length&&length!=count)throw new InvalidOperationException();ct.ThrowIfCancellationRequested();return buffer.AsSpan(0,count).ToArray();
        }
        finally{CryptographicOperations.ZeroMemory(buffer);}
    }
    private static void ValidateIdentityXml(byte[] bytes)
    {
        using var input=new MemoryStream(bytes,false);
        using var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16384,CheckCharacters=true});
        // Read at bounded depth before constructing any XML tree.
        while(reader.Read())if(reader.Depth>8)throw new InvalidOperationException();
        input.Position=0;
        using var again=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16384,CheckCharacters=true});
        var root=XDocument.Load(again).Root??throw new InvalidOperationException();XNamespace ns="https://sts.amazonaws.com/doc/2011-06-15/";
        if(root.Name!=ns+"GetCallerIdentityResponse" || root.DescendantsAndSelf().Any(x=>x.Attributes().Any(a=>!a.IsNamespaceDeclaration)))throw new InvalidOperationException();
        var result=root.Element(ns+"GetCallerIdentityResult");var metadata=root.Element(ns+"ResponseMetadata");
        if(root.Elements().Count()!=2 || result is null || metadata is null || result.Elements().Count()!=3 || new[]{"Arn","UserId","Account"}.Any(x=>result.Elements(ns+x).Count()!=1) || result.Elements().Any(x=>x.HasElements) || metadata.Elements().Count()!=1 || metadata.Element(ns+"RequestId") is not {HasElements:false})throw new InvalidOperationException();
    }
    protected override void Dispose(bool disposing)
    {base.Dispose(disposing);if(disposing&&retained is not null)CryptographicOperations.ZeroMemory(retained);}
}
