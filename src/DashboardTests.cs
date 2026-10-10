using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace OmenLCDControl {
    internal static class DashboardTests {
        private static void Check(bool condition,string error){if(!condition)throw new Exception(error);}
        internal static void Run(List<string> report,string root){
            var hub=new MetricHub();var ingest=new CustomMetricServer(hub,0,"test-token");
            ingest.Ingest("{\"metrics\":[{\"id\":\"custom.fps\",\"value\":144,\"unit\":\"fps\",\"ttlSeconds\":2},{\"id\":\"custom.status\",\"text\":\"Rendering\"}]}");
            var snapshot=hub.Snapshot();Check(snapshot["custom.fps"].Value==144&&snapshot["custom.status"].Text=="Rendering","Custom readings");
            Check(DashboardRenderer.Display(snapshot["custom.fps"],DateTime.UtcNow.AddSeconds(3))=="--","Stale data must expire");
            try{ingest.Ingest("{\"metrics\":[{\"id\":\"custom.fps\",\"value\":12},{\"id\":\"cpu.usage\",\"value\":99}]}");throw new Exception("Expected reserved metric rejection");}catch(ArgumentException){}
            Check(hub.Snapshot()["custom.fps"].Value==144,"Rejected batch must not partially publish");
            try{ingest.Ingest("{\"metrics\":[{\"id\":\"custom.x\",\"value\":\"NaN\"}]}");throw new Exception("Expected NaN rejection");}catch(ArgumentException){}
            report.Add("PASS custom numeric/text ingestion, reserved namespace protection, batch validation, and stale-data expiration");
            var config=new DashboardConfig();hub.Set("cpu.usage",48,null,"fixture");hub.Set("gpu.usage",72,null,"fixture");hub.Set("cpu.temperature",61,null,"fixture");hub.Set("gpu.temperature",55,null,"fixture");
            Check(DashboardRenderer.Fraction(config.Slots[0],hub.Snapshot()["cpu.usage"],DateTime.UtcNow)==.48,"Gauge scaling");
            hub.Set("cpu.usage",200,null,"fixture");Check(DashboardRenderer.Fraction(config.Slots[0],hub.Snapshot()["cpu.usage"],DateTime.UtcNow)==1,"Gauge clamping");
            foreach(string layout in new[]{"Rings","Bars","Spotlight"}){
                config.Layout=layout;using(var image=DashboardRenderer.Render(config,hub.Snapshot(),.3,DateTime.UtcNow)){Check(image.Width==480&&image.Height==480,"Dashboard dimensions");image.Save(Path.Combine(root,layout+".png"));}
            }
            config.Layout="Spotlight";using(var first=DashboardRenderer.Render(config,hub.Snapshot(),0,DateTime.UtcNow))using(var second=DashboardRenderer.Render(config,hub.Snapshot(),.8,DateTime.UtcNow)){
                byte[] a,b;using(var stream=new MemoryStream()){first.Save(stream,System.Drawing.Imaging.ImageFormat.Png);a=stream.ToArray();}using(var stream=new MemoryStream()){second.Save(stream,System.Drawing.Imaging.ImageFormat.Png);b=stream.ToArray();}Check(Convert.ToBase64String(a)!=Convert.ToBase64String(b),"Spotlight should advance");
            }
            report.Add("PASS ring/bar/spotlight rendering, gauge clamping, and slideshow advancement");
            var portProbe=new TcpListener(IPAddress.Loopback,0);portProbe.Start();int port=((IPEndPoint)portProbe.LocalEndpoint).Port;portProbe.Stop();
            using(var server=new CustomMetricServer(hub,port,"local-test-secret")){
                server.Start();
                var response=Http(port,null,"{\"metrics\":[{\"id\":\"custom.fps\",\"value\":1}]}");Check(response.StartsWith("HTTP/1.1 401"),"API auth required");
                response=Http(port,"local-test-secret","{\"metrics\":[{\"id\":\"custom.fps\",\"value\":165,\"unit\":\"fps\"}]}");Check(response.StartsWith("HTTP/1.1 200")&&hub.Snapshot()["custom.fps"].Value==165,"API real socket publish");
                response=Http(port,"local-test-secret","{\"metrics\":[{\"id\":\"cpu.usage\",\"value\":10}]}");Check(response.StartsWith("HTTP/1.1 400"),"API reserved namespace rejected");
            }
            report.Add("PASS loopback HTTP requests require token, publish valid values, and reject built-in overrides");
        }
        private static string Http(int port,string token,string data){
            using(var client=new TcpClient()){client.Connect(IPAddress.Loopback,port);client.ReceiveTimeout=3000;using(var stream=client.GetStream()){
                byte[] body=Encoding.UTF8.GetBytes(data);byte[] head=Encoding.ASCII.GetBytes("POST /v1/metrics HTTP/1.1\r\nHost: 127.0.0.1\r\n"+(token==null?"":"Authorization: Bearer "+token+"\r\n")+"Content-Length: "+body.Length+"\r\nContent-Type: application/json\r\n\r\n");stream.Write(head,0,head.Length);stream.Write(body,0,body.Length);using(var reader=new StreamReader(stream))return reader.ReadToEnd();
            }}
        }
        internal static void RenderExamples(string folder){
            Directory.CreateDirectory(folder);var hub=new MetricHub();hub.Set("cpu.usage",42,null,"Example");hub.Set("gpu.usage",76,null,"Example");hub.Set("cpu.temperature",62,null,"Example");hub.Set("gpu.temperature",58,null,"Example");hub.PutCustom("custom.fps","Game FPS","fps",144,null,30);hub.PutCustom("custom.status","Status","",null,"IN GAME",30);
            var config=new DashboardConfig();foreach(string layout in new[]{"Rings","Bars","Spotlight"}){config.Layout=layout;using(var image=DashboardRenderer.Render(config,hub.Snapshot(),0,DateTime.UtcNow))image.Save(Path.Combine(folder,layout.ToLowerInvariant()+".png"));}
        }
    }
}
