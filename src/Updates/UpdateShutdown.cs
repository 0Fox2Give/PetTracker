namespace PetTracker.Updates;

public static class UpdateShutdown {
 // Do not kill the capture worker to force installation. A failed save/exit
 // leaves the running version available and asks the player to retry later.
 public static async Task<bool> Apply(Func<Task<bool>> saveAndStopWorker,Func<bool> workerHasExited,Action apply,CancellationToken cancellation){
  cancellation.ThrowIfCancellationRequested();
  if(!await saveAndStopWorker())return false;
  var deadline=DateTime.UtcNow.AddSeconds(10);
  while(!workerHasExited()){if(DateTime.UtcNow>=deadline)return false;await Task.Delay(100,cancellation);}
  cancellation.ThrowIfCancellationRequested();apply();return true;
 }
}
