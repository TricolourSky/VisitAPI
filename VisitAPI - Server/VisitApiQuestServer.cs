using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;

namespace VisitAPI.Server;

[Injectable(/*Could not decode attribute arguments.*/)]
public class VisitApiQuestServer : IOnLoad
{
	private readonly ISptLogger<VisitApiQuestServer> _logger;

	private readonly VisitApiQuestHelper _questHelper;

	public VisitApiQuestServer(ISptLogger<VisitApiQuestServer> logger, VisitApiQuestHelper questHelper)
	{
		_logger = logger;
		_questHelper = questHelper;
	}

	public Task OnLoad()
	{
		HttpListener httpListener = new HttpListener();
		httpListener.Prefixes.Add("http://127.0.0.1:6970/visitapi/");
		try
		{
			httpListener.Start();
			RunAsync(httpListener);
		}
		catch (Exception ex)
		{
			_logger.Error("VisitAPI quest server failed to start: " + ex.Message, (Exception)null);
		}
		return Task.CompletedTask;
	}

	private async Task RunAsync(HttpListener listener)
	{
		while (listener.IsListening)
		{
			try
			{
				_ = HandleAsync(await listener.GetContextAsync());
			}
			catch (Exception ex) when (!(ex is ObjectDisposedException))
			{
				_logger.Warning("VisitAPI listener error: " + ex.Message, (Exception)null);
			}
		}
	}

	private Task<string> RouteAsync(string path, string body)
	{
		if (path.EndsWith("/quest/accept", StringComparison.OrdinalIgnoreCase))
		{
			return _questHelper.AcceptQuestAsync(body);
		}
		if (path.EndsWith("/quest/handover", StringComparison.OrdinalIgnoreCase))
		{
			return _questHelper.HandoverQuestAsync(body);
		}
		if (path.EndsWith("/quest/complete", StringComparison.OrdinalIgnoreCase))
		{
			return _questHelper.CompleteQuestAsync(body);
		}
		if (path.EndsWith("/quest/status", StringComparison.OrdinalIgnoreCase))
		{
			return _questHelper.GetQuestStatusAsync(body);
		}
		if (path.EndsWith("/quest/sync", StringComparison.OrdinalIgnoreCase))
		{
			return _questHelper.SyncQuestTransitionsAsync(body);
		}
		return Task.FromResult("{\"success\":false,\"error\":\"unknown endpoint\"}");
	}

	private async Task HandleAsync(HttpListenerContext ctx)
	{
		ctx.Response.ContentType = "application/json; charset=utf-8";
		ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
		string s;
		try
		{
			using StreamReader reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
			string body = await reader.ReadToEndAsync();
			s = await RouteAsync(ctx.Request.Url?.AbsolutePath ?? "", body);
		}
		catch (Exception ex)
		{
			_logger.Error("HandleAsync error: " + ex.Message, (Exception)null);
			s = "{\"success\":false,\"error\":\"" + ex.Message.Replace("\"", "'") + "\"}";
		}
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		ctx.Response.ContentLength64 = bytes.Length;
		await ctx.Response.OutputStream.WriteAsync(bytes);
		ctx.Response.Close();
	}
}
