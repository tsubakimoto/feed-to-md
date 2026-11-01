#!/usr/bin/env dotnet
#:property TargetFramework=net10.0
#:property PublishAot=false
#:include FeedToMarkdown.cs

return await FeedToMarkdown.Application.RunAsync(args);
