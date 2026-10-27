using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DiscordChatExporter.Core.Discord;
using DiscordChatExporter.Core.Discord.Data;
using DiscordChatExporter.Core.Exceptions;
using DiscordChatExporter.Core.Exporting;
using DiscordChatExporter.Core.Exporting.Filtering;
using DiscordChatExporter.Core.Exporting.Partitioning;
using FluentAssertions;
using Xunit;

namespace DiscordChatExporter.Cli.Tests.Specs;

public sealed class ChannelMentionSpecs
{
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "unavailable-channel")]
    [InlineData(HttpStatusCode.NotFound, "deleted-channel")]
    public async Task Json_export_continues_after_forwarded_mentions_of_inaccessible_channels(
        HttpStatusCode statusCode,
        string channelName
    )
    {
        using var handler = new ChannelHttpMessageHandler(statusCode);
        using var httpClient = new HttpClient(handler);
        using var discord = CreateClient(httpClient);
        var context = CreateContext(discord);
        const string content = "Please <#123> before the deadline. <#456> @everyone!";
        using var messageJson = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    id = "1000",
                    type = 0,
                    flags = 16384,
                    author = new
                    {
                        id = "10",
                        username = "test-user",
                        discriminator = "0",
                    },
                    timestamp = "2026-01-19T18:26:46.502Z",
                    content = "",
                    message_reference = new
                    {
                        type = 1,
                        message_id = "999",
                        channel_id = "2",
                    },
                    message_snapshots = new[]
                    {
                        new { message = new { timestamp = "2026-01-19T17:41:23.425Z", content } },
                    },
                }
            )
        );
        var forwarded = Message.Parse(messageJson.RootElement);
        using var stream = new MemoryStream();
        await using var writer = new JsonMessageWriter(stream, context);

        await writer.WritePreambleAsync();
        await writer.WriteMessageAsync(forwarded);
        await writer.WriteMessageAsync(
            forwarded with
            {
                Id = new Snowflake(1001),
                Content = "After the forward: <#123>",
                Reference = null,
                ForwardedMessage = null,
            }
        );
        await writer.WritePostambleAsync();

        using var export = JsonDocument.Parse(stream.ToArray());
        var messages = export.RootElement.GetProperty("messages");
        messages.GetArrayLength().Should().Be(2);
        export.RootElement.GetProperty("messageCount").GetInt32().Should().Be(2);
        var snapshot = messages[0].GetProperty("forwardedMessage");
        snapshot
            .GetProperty("content")
            .GetString()
            .Should()
            .Be($"Please #{channelName} before the deadline. #{channelName} @everyone!");
        snapshot.GetProperty("contentRaw").GetString().Should().Be(content);
        messages[1]
            .GetProperty("content")
            .GetString()
            .Should()
            .Be($"After the forward: #{channelName}");
        handler
            .RequestPaths.Should()
            .Equal("/api/v10/users/@me", "/api/v10/channels/123", "/api/v10/channels/456");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "unavailable-channel")]
    [InlineData(HttpStatusCode.NotFound, "deleted-channel")]
    public async Task Html_mentions_distinguish_forbidden_and_deleted_channels(
        HttpStatusCode statusCode,
        string channelName
    )
    {
        using var handler = new ChannelHttpMessageHandler(statusCode);
        using var httpClient = new HttpClient(handler);
        using var discord = CreateClient(httpClient);

        var html = await HtmlMarkdownVisitor.FormatAsync(CreateContext(discord), "See <#123>");

        html.Should().Contain($">#{channelName}</span>");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Optional_channel_lookup_still_reports_other_errors(HttpStatusCode statusCode)
    {
        using var handler = new ChannelHttpMessageHandler(statusCode);
        using var httpClient = new HttpClient(handler);
        using var discord = CreateClient(httpClient);

        var act = async () => await discord.TryGetChannelAsync(new Snowflake(123));

        await act.Should().ThrowAsync<DiscordChatExporterException>();
    }

    [Fact]
    public async Task Required_channel_lookup_still_reports_forbidden()
    {
        using var handler = new ChannelHttpMessageHandler(HttpStatusCode.Forbidden);
        using var httpClient = new HttpClient(handler);
        using var discord = CreateClient(httpClient);

        var act = async () => await discord.GetChannelAsync(new Snowflake(123));

        await act.Should().ThrowAsync<DiscordChatExporterException>().WithMessage("*forbidden*");
    }

    private static DiscordClient CreateClient(HttpClient httpClient) =>
        new(
            "test-token",
            RateLimitPreference.IgnoreAll,
            httpClient,
            (_, _) => ValueTask.CompletedTask
        );

    private static ExportContext CreateContext(DiscordClient discord) =>
        new(
            discord,
            new ExportRequest(
                Guild.DirectMessages,
                new Channel(
                    new Snowflake(2),
                    ChannelKind.DirectGroupTextChat,
                    Guild.DirectMessages.Id,
                    null,
                    "test-group",
                    null,
                    null,
                    null,
                    false,
                    null
                ),
                Path.Combine(Path.GetTempPath(), "channel-mention-test.json"),
                null,
                ExportFormat.Json,
                null,
                null,
                PartitionLimit.Null,
                MessageFilter.Null,
                isReverseMessageOrder: false,
                shouldFormatMarkdown: true,
                shouldDownloadAssets: false,
                shouldReuseAssets: false,
                locale: "en-US",
                isUtcNormalizationEnabled: true
            )
        );

    private sealed class ChannelHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            RequestPaths.Add(path);
            return Task.FromResult(
                new HttpResponseMessage(
                    path == "/api/v10/users/@me" ? HttpStatusCode.OK : statusCode
                )
                {
                    Content = new StringContent("{}"),
                }
            );
        }
    }
}
