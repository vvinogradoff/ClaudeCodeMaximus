using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Tessyn.Web.Auth;
using Tessyn.Web.Link;
using Tessyn.Web.Models;
using Tessyn.Web.Options;

namespace Tessyn.Web.Endpoints;

/// <summary>
/// Attachment upload (decision A12). <c>POST /api/attachments</c> (browser session): the file is streamed into the blob store, the daemon
/// is asked over the link to fetch it (<c>attachment.fetch</c>), and the daemon's reply is returned to the browser. <c>GET /link/blob/{id}</c>
/// (link token): the daemon's one-time download of that blob, deleted afterwards.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class AttachmentEndpoints
{
	public static void MapAttachmentEndpoints(this IEndpointRouteBuilder routes)
	{
		routes.MapPost(Constants.Paths.Attachments, (Delegate)UploadAsync).RequireAuthorization();
		routes.MapGet(Constants.Paths.LinkBlob, (Delegate)DownloadAsync);
	}

	private static async Task<IResult> UploadAsync(
		HttpContext context,
		DaemonLinkHub hub,
		BlobStore blobs,
		WireJson json,
		IOptions<TessynWebOptions> options,
		ILoggerFactory loggers)
	{
		var logger = loggers.CreateLogger(typeof(AttachmentEndpoints));
		var settings = options.Value.Attachments;
		var boundary = BoundaryOf(context.Request.ContentType);
		if (boundary == null)
			return Error(StatusCodes.Status400BadRequest, Constants.Attachments.NotMultipartMessage);

		// The browser would otherwise push up to 200 MB before it learns that nothing can take the file.
		if (!hub.IsOnline)
			return Error(StatusCodes.Status503ServiceUnavailable, Constants.Attachments.DaemonOfflineMessage);

		var cancellation = context.RequestAborted;
		StoredBlob? blob = null;
		try
		{
			var reader = new MultipartReader(boundary, context.Request.Body)
			{
				BodyLengthLimit = settings.MaxUploadBytes + Constants.Limits.MultipartOverheadBytes,
			};

			string? nodeId = null;
			string? name = null;
			MultipartSection? section;
			while ((section = await reader.ReadNextSectionAsync(cancellation)) != null)
			{
				if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
					continue;

				var field = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
				if (field == Constants.Attachments.NodeIdField)
				{
					using var text = new StreamReader(section.Body);
					nodeId = (await text.ReadToEndAsync(cancellation)).Trim();
					if (nodeId.Length == 0 || nodeId.Length > Constants.Limits.NodeIdMaxLength)
						return Error(StatusCodes.Status400BadRequest, Constants.Attachments.MissingNodeIdMessage);
				}
				else if (field == Constants.Attachments.FileField)
				{
					if (nodeId == null)
						return Error(StatusCodes.Status400BadRequest, Constants.Attachments.MissingNodeIdMessage);

					name = FileNameOf(disposition);
					blob = await blobs.SaveAsync(section.Body, settings.MaxUploadBytes, cancellation);
					break;
				}
			}

			if (blob == null || nodeId == null)
				return Error(StatusCodes.Status400BadRequest, Constants.Attachments.MissingFileMessage);

			var request = JsonSerializer.SerializeToElement(
				new FetchParams(blob.Id, nodeId, name ?? Constants.Attachments.FallbackName, blob.Size, blob.Sha256), json.Options);
			var outcome = await hub.SendAsync(
				Constants.Remote.Methods.AttachmentFetch, request, cancellation, TimeSpan.FromSeconds(settings.FetchTimeoutSeconds));

			return Reply(outcome, logger);
		}
		catch (BlobTooLargeException)
		{
			return Error(StatusCodes.Status413PayloadTooLarge, Constants.Attachments.TooLargeMessage);
		}
		catch (Exception ex) when (!cancellation.IsCancellationRequested && ex is InvalidDataException or IOException or BadHttpRequestException)
		{
			// A body over the limit, a cut multipart stream, a malformed part.
			logger.LogWarning(ex, "Attachment upload could not be read");
			var overLimit = ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
				|| (ex is InvalidDataException && ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase));
			return overLimit
				? Error(StatusCodes.Status413PayloadTooLarge, Constants.Attachments.TooLargeMessage)
				: Error(StatusCodes.Status400BadRequest, Constants.Attachments.BadUploadMessage);
		}
		finally
		{
			if (blob != null)
				blobs.Delete(blob.Id);
		}
	}

	private static IResult Reply(RpcOutcome outcome, ILogger logger)
	{
		if (!outcome.IsError)
			return Results.Text(outcome.ResultJson ?? "null", Constants.ContentTypes.Json);

		JsonRpcErrorBody? error = null;
		try
		{
			error = JsonSerializer.Deserialize<JsonRpcErrorBody>(outcome.ErrorJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		}
		catch (JsonException)
		{
			logger.LogWarning("Unreadable error from the daemon for attachment.fetch");
		}

		return error?.Code switch
		{
			Constants.JsonRpc.DaemonOffline => Error(StatusCodes.Status503ServiceUnavailable, Constants.Attachments.DaemonOfflineMessage),
			Constants.JsonRpc.DaemonTimeout => Error(StatusCodes.Status504GatewayTimeout, Constants.Attachments.DaemonTimeoutMessage),
			null => Error(StatusCodes.Status502BadGateway, Constants.Attachments.BadReplyMessage),
			_ => Error(StatusCodes.Status422UnprocessableEntity, Constants.Attachments.DaemonRefusedPrefix + error.Message),
		};
	}

	private static async Task DownloadAsync(HttpContext context, string blobId, LinkTokenValidator tokens, BlobStore blobs)
	{
		if (!tokens.IsAuthorized(context.Request))
		{
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			return;
		}

		if (!blobs.TryClaim(blobId, out var blob))
		{
			context.Response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}

		try
		{
			await using var source = new FileStream(blob.Path, FileMode.Open, FileAccess.Read, FileShare.Read, Constants.Limits.CopyBufferBytes, useAsync: true);
			context.Response.ContentType = Constants.Attachments.OctetStream;
			context.Response.ContentLength = source.Length;
			await source.CopyToAsync(context.Response.Body, Constants.Limits.CopyBufferBytes, context.RequestAborted);
		}
		catch (OperationCanceledException)
		{
			// The daemon went away mid-download.
		}
		catch (IOException)
		{
			// Connection dropped or the file vanished.
		}
		finally
		{
			blobs.Delete(blobId);
		}
	}

	private static string? BoundaryOf(string? contentType)
	{
		if (string.IsNullOrEmpty(contentType) || !MediaTypeHeaderValue.TryParse(contentType, out var media)
		    || !media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
			return null;

		var boundary = HeaderUtilities.RemoveQuotes(media.Boundary).Value;
		return string.IsNullOrEmpty(boundary) ? null : boundary;
	}

	private static string FileNameOf(ContentDispositionHeaderValue disposition)
	{
		var raw = (disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
		var name = HeaderUtilities.RemoveQuotes(raw).Value;
		return string.IsNullOrWhiteSpace(name) ? Constants.Attachments.FallbackName : name;
	}

	private static IResult Error(int status, string message) =>
		Results.Json(new ErrorResponse(message), statusCode: status);

	private sealed record FetchParams(string BlobId, string NodeId, string Name, long Size, string Sha256);
}
