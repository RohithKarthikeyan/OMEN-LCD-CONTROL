using System;
using System.Threading;
using System.Threading.Tasks;

namespace OmenLCDControl {
    public sealed class DashboardRuntime : IDisposable {
        public readonly MetricHub Hub=new MetricHub();
        private readonly SystemMetrics metrics;
        private readonly CancellationTokenSource stop=new CancellationTokenSource();
        private readonly Task polling;
        public readonly CustomMetricServer Server;
        public string ApiStatus;
        public string SensorStatus{get{return metrics.SensorStatus;}}
        public string GpuStatus{get{return metrics.GpuStatus;}}
        public DashboardRuntime(){
            metrics=new SystemMetrics(Hub);
            try{Server=new CustomMetricServer(Hub,47845,CustomMetricServer.LoadToken());Server.Start();ApiStatus="Listening on "+Server.Url;}
            catch(Exception e){ApiStatus="Custom API unavailable: "+e.Message;AppState.Log(e);}
            polling=Task.Run(async()=>{
                try{while(!stop.IsCancellationRequested){try{metrics.Poll();}catch(Exception e){AppState.Log(e);}await Task.Delay(1000,stop.Token);}}
                catch(OperationCanceledException){}finally{metrics.Dispose();}
            });
        }
        public void Dispose(){if(Server!=null)Server.Dispose();stop.Cancel();/* The poll task disposes its counters when its pending read finishes. */}
    }
}
