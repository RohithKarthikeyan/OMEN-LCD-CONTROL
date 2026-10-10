using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OmenLCDControl {
    public sealed class CustomMetricServer : IDisposable {
        private readonly MetricHub hub;
        private readonly TcpListener listener;
        private volatile bool stopped;
        private readonly SemaphoreSlim workers=new SemaphoreSlim(4);
        public readonly string Token;
        public readonly int Port;
        public string Url{get{return "http://127.0.0.1:"+Port;}}
        public CustomMetricServer(MetricHub target,int port,string token){hub=target;Port=port;Token=token;listener=new TcpListener(IPAddress.Loopback,port);}
        public static string LoadToken(){
            string path=Path.Combine(AppState.Root,"api-token.txt");Directory.CreateDirectory(AppState.Root);
            if(File.Exists(path)){string token=File.ReadAllText(path).Trim();if(token.Length==64)return token;}
            byte[] bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);
            string value=BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();File.WriteAllText(path,value);return value;
        }
        public void Start(){listener.Start(8);Task.Run((Func<Task>)Accept);}
        private async Task Accept(){
            while(!stopped){
                try{
                    var client=await listener.AcceptTcpClientAsync();
                    if(!workers.Wait(0)){client.Close();continue;}
                    ServeLater(client);
                }catch(ObjectDisposedException){break;}catch(SocketException){if(stopped)break;}
            }
        }
        private void ServeLater(TcpClient client){Task.Run(()=>{try{Serve(client);}finally{workers.Release();}});}
        private static string Line(Stream stream,int max){
            var bytes=new List<byte>();int c;
            while((c=stream.ReadByte())!=-1){if(c==10)return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');bytes.Add((byte)c);if(bytes.Count>max)throw new ArgumentException("Header too long.");}
            throw new IOException("Incomplete request.");
        }
        private void Serve(TcpClient client){
            using(client){client.ReceiveTimeout=2000;client.SendTimeout=2000;using(var stream=client.GetStream()){
                try{
                    string request=Line(stream,2048);string[] parts=request.Split(' ');if(parts.Length!=3)throw new ArgumentException("Invalid request.");
                    var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);int size=0;string line;
                    while((line=Line(stream,4096)).Length>0){size+=line.Length;if(size>8192)throw new ArgumentException("Headers too large.");int colon=line.IndexOf(':');if(colon<1)throw new ArgumentException("Invalid header.");string key=line.Substring(0,colon);if(headers.ContainsKey(key))throw new ArgumentException("Duplicate header.");headers[key]=line.Substring(colon+1).Trim();}
                    // Browsers are not granted CORS access. Requests require the locally stored bearer token.
                    string auth;if(!headers.TryGetValue("Authorization",out auth)||!TokenEquals(auth,"Bearer "+Token)){DrainBody(stream,headers);Reply(stream,401,"{\"error\":\"Bearer token required\"}");return;}
                    if(parts[0]=="GET"&&parts[1]=="/v1/metrics"){Reply(stream,200,new JavaScriptSerializer().Serialize(hub.Snapshot().Values));return;}
                    if(parts[0]!="POST"||parts[1]!="/v1/metrics"){Reply(stream,404,"{\"error\":\"Use POST or GET /v1/metrics\"}");return;}
                    string lengthText;int length;
                    if(headers.ContainsKey("Transfer-Encoding")||!headers.TryGetValue("Content-Length",out lengthText)||!int.TryParse(lengthText,out length)||length<1||length>65536)throw new ArgumentException("Content-Length must be between 1 and 65536. Chunked requests are unsupported.");
                    byte[] body=new byte[length];int read=0;while(read<length){int n=stream.Read(body,read,length-read);if(n==0)throw new IOException("Incomplete body.");read+=n;}
                    string data=new UTF8Encoding(false,true).GetString(body);
                    int count=Ingest(data);Reply(stream,200,"{\"accepted\":"+count+"}");
                }catch(ArgumentException e){try{Reply(stream,400,new JavaScriptSerializer().Serialize(new{error=e.Message}));}catch{ }}catch(Exception){try{Reply(stream,400,"{\"error\":\"Invalid or incomplete request\"}");}catch{ }}
            }}
        }
        private static void DrainBody(Stream stream,Dictionary<string,string> headers){
            string text;int length;if(!headers.TryGetValue("Content-Length",out text)||!int.TryParse(text,out length)||length<0||length>65536)return;
            byte[] buffer=new byte[4096];while(length>0){int n=stream.Read(buffer,0,Math.Min(length,buffer.Length));if(n<=0)return;length-=n;}
        }
        private static bool TokenEquals(string a,string b){if(a.Length!=b.Length)return false;int diff=0;for(int i=0;i<a.Length;i++)diff|=a[i]^b[i];return diff==0;}
        public int Ingest(string text){
            var root=new JavaScriptSerializer{MaxJsonLength=65536,RecursionLimit=12}.Deserialize<Dictionary<string,object>>(text);
            object raw;if(root==null||!root.TryGetValue("metrics",out raw))throw new ArgumentException("Expected an object with a metrics array.");
            var rows=raw as System.Collections.IEnumerable;if(rows==null)throw new ArgumentException("metrics must be an array.");
            var batch=new List<Tuple<string,string,string,double?,string,int>>();
            foreach(var row in rows){
                var obj=row as Dictionary<string,object>;if(obj==null)throw new ArgumentException("Invalid metric object.");
                string id=GetString(obj,"id",null),label=GetString(obj,"label",id),unit=GetString(obj,"unit","");object valueObj;double? value=null;
                if(obj.TryGetValue("value",out valueObj)&&valueObj!=null){double parsed;if(!double.TryParse(Convert.ToString(valueObj,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out parsed))throw new ArgumentException("value must be numeric.");value=parsed;}
                string status=GetString(obj,"text",null);int ttl=5;object ttlObj;if(obj.TryGetValue("ttlSeconds",out ttlObj)){double number=Convert.ToDouble(ttlObj,CultureInfo.InvariantCulture);if(number!=Math.Floor(number)||number<1||number>86400)throw new ArgumentException("Invalid ttlSeconds.");ttl=(int)number;}
                // Validate the entire batch before publishing any readings.
                var validator=new MetricHub();validator.PutCustom(id,label,unit,value,status,ttl);
                batch.Add(Tuple.Create(id,label,unit,value,status,ttl));if(batch.Count>64)throw new ArgumentException("At most 64 metrics per request.");
            }
            if(batch.Count==0)throw new ArgumentException("metrics cannot be empty.");
            hub.PutBatch(batch);
            return batch.Count;
        }
        private static string GetString(Dictionary<string,object> row,string key,string fallback){object value;if(!row.TryGetValue(key,out value)||value==null)return fallback;if(!(value is string))throw new ArgumentException(key+" must be text.");return(string)value;}
        private static void Reply(Stream stream,int code,string body){byte[] data=Encoding.UTF8.GetBytes(body);byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+" "+(code==200?"OK":code==401?"Unauthorized":code==404?"Not Found":"Bad Request")+"\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "+data.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(data,0,data.Length);}
        public void Dispose(){stopped=true;listener.Stop();}
    }
}
