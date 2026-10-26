using IntakeLog.Web;
using IntakeLog.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton(new HttpClient());
builder.Services.AddSingleton<AiFoodService>();

await builder.Build().RunAsync();
