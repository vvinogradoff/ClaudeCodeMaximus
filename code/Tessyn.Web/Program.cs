using Microsoft.Extensions.Options;
using Tessyn.Web;
using Tessyn.Web.Auth;
using Tessyn.Web.Cli;
using Tessyn.Web.Endpoints;
using Tessyn.Web.Hosting;
using Tessyn.Web.Options;

var cliExitCode = new CliRunner(new PasswordHasher())
	.TryRun(args, Console.In, Console.Out, Console.Error, interactive: !Console.IsInputRedirected);
if (cliExitCode.HasValue)
	return cliExitCode.Value;

var builder = WebApplication.CreateBuilder(args);

var extraConfig = builder.Configuration[Constants.Config.ExtraConfigVariable];
if (!string.IsNullOrWhiteSpace(extraConfig))
	builder.Configuration.AddJsonFile(extraConfig, optional: true, reloadOnChange: false);

builder.AddTessynWeb();
var app = builder.Build();

var problems = app.Services.GetRequiredService<TessynWebOptionsValidator>()
	.Validate(app.Services.GetRequiredService<IOptions<TessynWebOptions>>().Value);
if (problems.Count > 0)
{
	foreach (var problem in problems)
		Console.Error.WriteLine(problem);

	return 1;
}

app.UseTessynWeb();
app.MapTessynEndpoints();
await app.RunAsync();
return 0;
