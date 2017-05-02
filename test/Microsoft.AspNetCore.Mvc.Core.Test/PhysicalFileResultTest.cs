// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.AspNetCore.Mvc.TestCommon;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Mvc
{
    public class PhysicalFileResultTest
    {
        [Fact]
        public void Constructor_SetsFileName()
        {
            // Arrange
            var path = Path.GetFullPath("helllo.txt");

            // Act
            var result = new TestPhysicalFileResult(path, "text/plain");

            // Assert
            Assert.Equal(path, result.FileName);
        }

        [Fact]
        public void Constructor_SetsContentTypeAndParameters()
        {
            // Arrange
            var path = Path.GetFullPath("helllo.txt");
            var contentType = "text/plain; charset=us-ascii; p1=p1-value";
            var expectedMediaType = contentType;

            // Act
            var result = new TestPhysicalFileResult(path, contentType);

            // Assert
            Assert.Equal(path, result.FileName);
            MediaTypeAssert.Equal(expectedMediaType, result.ContentType);
        }

        [Theory]
        [InlineData(0, 3, "File", 4)]
        [InlineData(8, 13, "Result", 6)]
        [InlineData(null, 3, "File", 4)]
        [InlineData(8, null, "ResultTestFile contents�", 26)]
        public async Task WriteFileAsync_WritesRangeRequested(long? start, long? end, string expectedString, long contentLength)
        {
            // Arrange
            var path = Path.GetFullPath(Path.Combine("TestFiles", "FilePathResultTestFile.txt"));
            var result = new TestPhysicalFileResult(path, "text/plain");
            var httpContext = GetHttpContext();
            var requestHeaders = httpContext.Request.GetTypedHeaders();
            requestHeaders.IfModifiedSince = DateTimeOffset.MinValue;
            requestHeaders.Range = new RangeHeaderValue(start, end);
            httpContext.Request.Method = HttpMethods.Get;
            httpContext.Response.Body = new MemoryStream();
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            // Act
            await result.ExecuteResultAsync(actionContext);

            // Assert
            var httpResponse = actionContext.HttpContext.Response;
            httpResponse.Body.Seek(0, SeekOrigin.Begin);
            var streamReader = new StreamReader(httpResponse.Body);
            var body = streamReader.ReadToEndAsync().Result;
            if (!start.HasValue)
            {
                start = 0;
            }
            if (!end.HasValue)
            {
                end = 33;
            }
            var contentRange = new ContentRangeHeaderValue(start.Value, end.Value, 34);
            Assert.Equal(StatusCodes.Status206PartialContent, httpResponse.StatusCode);
            Assert.Equal("bytes", httpResponse.Headers[HeaderNames.AcceptRanges]);
            Assert.Equal(contentRange.ToString(), httpResponse.Headers[HeaderNames.ContentRange]);
            Assert.NotEmpty(httpResponse.Headers[HeaderNames.LastModified]);
            Assert.Equal(contentLength, httpResponse.ContentLength);
            Assert.Equal(expectedString, body);
        }

        [Theory]
        [InlineData("0-5")]
        [InlineData("bytes = 11-0")]
        [InlineData("bytes = 1-4, 5-11")]
        public async Task WriteFileAsync_RangeRequested_NotSatisfiable(string rangeString)
        {
            // Arrange
            var lastModified = DateTimeOffset.UtcNow;
            lastModified = new DateTimeOffset(lastModified.Year, lastModified.Month, lastModified.Day, lastModified.Hour, lastModified.Minute, lastModified.Second, TimeSpan.FromSeconds(0));
            var path = Path.GetFullPath(Path.Combine("TestFiles", "FilePathResultTestFile.txt"));
            var result = new TestPhysicalFileResult(path, "text/plain");
            var httpContext = GetHttpContext();
            var requestHeaders = httpContext.Request.GetTypedHeaders();
            requestHeaders.IfModifiedSince = DateTimeOffset.MinValue;
            httpContext.Request.Headers[HeaderNames.Range] = rangeString;
            httpContext.Request.Method = HttpMethods.Get;
            httpContext.Response.Body = new MemoryStream();
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            // Act
            await result.ExecuteResultAsync(actionContext);

            // Assert
            var httpResponse = actionContext.HttpContext.Response;
            httpResponse.Body.Seek(0, SeekOrigin.Begin);
            var streamReader = new StreamReader(httpResponse.Body);
            var body = streamReader.ReadToEndAsync().Result;
            var contentRange = new ContentRangeHeaderValue(34);
            Assert.Equal(StatusCodes.Status416RangeNotSatisfiable, httpResponse.StatusCode);
            Assert.Equal("bytes", httpResponse.Headers[HeaderNames.AcceptRanges]);
            Assert.Equal(contentRange.ToString(), httpResponse.Headers[HeaderNames.ContentRange]);
            Assert.NotEmpty(httpResponse.Headers[HeaderNames.LastModified]);
            Assert.Empty(body);
        }

        [Fact]
        public async Task WriteFileAsync_RangeRequested_PreconditionFailed()
        {
            // Arrange
            var path = Path.GetFullPath(Path.Combine("TestFiles", "FilePathResultTestFile.txt"));
            var result = new TestPhysicalFileResult(path, "text/plain");
            var httpContext = GetHttpContext();
            var requestHeaders = httpContext.Request.GetTypedHeaders();
            requestHeaders.IfUnmodifiedSince = DateTimeOffset.MinValue;
            httpContext.Request.Headers[HeaderNames.Range] = "bytes = 0-6";
            httpContext.Request.Method = HttpMethods.Get;
            httpContext.Response.Body = new MemoryStream();
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            // Act
            await result.ExecuteResultAsync(actionContext);

            // Assert
            var httpResponse = actionContext.HttpContext.Response;
            httpResponse.Body.Seek(0, SeekOrigin.Begin);
            var streamReader = new StreamReader(httpResponse.Body);
            var body = streamReader.ReadToEndAsync().Result;
            Assert.Equal(StatusCodes.Status412PreconditionFailed, httpResponse.StatusCode);
            Assert.Equal("bytes", httpResponse.Headers[HeaderNames.AcceptRanges]);
            Assert.Empty(httpResponse.Headers[HeaderNames.ContentRange]);
            Assert.NotEmpty(httpResponse.Headers[HeaderNames.LastModified]);
            Assert.Null(httpResponse.ContentLength);
            Assert.Empty(body);
        }

        [Fact]
        public async Task WriteFileAsync_RangeRequested_NotModified()
        {
            // Arrange
            var path = Path.GetFullPath(Path.Combine("TestFiles", "FilePathResultTestFile.txt"));
            var result = new TestPhysicalFileResult(path, "text/plain");
            var httpContext = GetHttpContext();
            var requestHeaders = httpContext.Request.GetTypedHeaders();
            requestHeaders.IfModifiedSince = DateTimeOffset.UtcNow;
            httpContext.Request.Headers[HeaderNames.Range] = "bytes = 0-6";
            httpContext.Request.Method = HttpMethods.Get;
            httpContext.Response.Body = new MemoryStream();
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            // Act
            await result.ExecuteResultAsync(actionContext);

            // Assert
            var httpResponse = actionContext.HttpContext.Response;
            httpResponse.Body.Seek(0, SeekOrigin.Begin);
            var streamReader = new StreamReader(httpResponse.Body);
            var body = streamReader.ReadToEndAsync().Result;
            Assert.Equal(StatusCodes.Status304NotModified, httpResponse.StatusCode);
            Assert.Equal("bytes", httpResponse.Headers[HeaderNames.AcceptRanges]);
            Assert.Empty(httpResponse.Headers[HeaderNames.ContentRange]);
            Assert.NotEmpty(httpResponse.Headers[HeaderNames.LastModified]);
            Assert.Null(httpResponse.ContentLength);
            Assert.Empty(body);
        }

        [Fact]
        public async Task ExecuteResultAsync_FallsbackToStreamCopy_IfNoIHttpSendFilePresent()
        {
            // Arrange
            var path = Path.GetFullPath(Path.Combine("TestFiles", "FilePathResultTestFile.txt"));
            var result = new TestPhysicalFileResult(path, "text/plain");
            var httpContext = GetHttpContext();
            httpContext.Response.Body = new MemoryStream();
            var context = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            // Act
            await result.ExecuteResultAsync(context);
            httpContext.Response.Body.Position = 0;

            // Assert
            Assert.NotNull(httpContext.Response.Body);
            var contents = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
            Assert.Equal("FilePathResultTestFile contents�", contents);
        }

        [Fact]
        public async Task ExecuteResultAsync_CallsSendFileAsync_IfIHttpSendFilePresent()
        {
            // Arrange
            var path = Path.GetFullPath(Path.Combine("TestFiles", "FilePathResultTestFile.txt"));
            var result = new TestPhysicalFileResult(path, "text/plain");
            var sendFileMock = new Mock<IHttpSendFileFeature>();
            sendFileMock
                .Setup(s => s.SendFileAsync(path, 0, null, CancellationToken.None))
                .Returns(Task.FromResult<int>(0));

            var httpContext = GetHttpContext();
            httpContext.Features.Set(sendFileMock.Object);
            var context = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            // Act
            await result.ExecuteResultAsync(context);

            // Assert
            sendFileMock.Verify();
        }

        [Theory]
        [InlineData(0, 3, "File", 4)]
        [InlineData(8, 13, "Result", 6)]
        [InlineData(null, 3, "File", 4)]
        [InlineData(8, null, "ResultTestFile contents¡", 26)]
        public async Task ExecuteResultAsync_CallsSendFileAsyncWithRequestedRange_IfIHttpSendFilePresent(long? start, long? end, string expectedString, long contentLength)
        {
            // Arrange
            var path = Path.GetFullPath(Path.Combine("TestFiles", "FilePathResultTestFile.txt"));
            var result = new TestPhysicalFileResult(path, "text/plain");

            var sendFile = new TestSendFileFeature();
            var httpContext = GetHttpContext();
            httpContext.Features.Set<IHttpSendFileFeature>(sendFile);
            var context = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var requestHeaders = httpContext.Request.GetTypedHeaders();
            requestHeaders.Range = new RangeHeaderValue(start, end);
            requestHeaders.IfUnmodifiedSince = DateTimeOffset.Now;
            httpContext.Request.Method = HttpMethods.Get;
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            // Act
            await result.ExecuteResultAsync(actionContext);

            // Assert
            var httpResponse = actionContext.HttpContext.Response;

            if (!start.HasValue)
            {
                start = 0;
            }
            if (!end.HasValue)
            {
                end = 33;
            }
            Assert.Equal(Path.GetFullPath(Path.Combine("TestFiles", "FilePathResultTestFile.txt")), sendFile.name);
            Assert.Equal(start, sendFile.offset);
            Assert.Equal(contentLength, sendFile.length);
            Assert.Equal(CancellationToken.None, sendFile.token);
            var contentRange = new ContentRangeHeaderValue(start.Value, end.Value, 34);
            Assert.Equal(StatusCodes.Status206PartialContent, httpResponse.StatusCode);
            Assert.Equal("bytes", httpResponse.Headers[HeaderNames.AcceptRanges]);
            Assert.Equal(contentRange.ToString(), httpResponse.Headers[HeaderNames.ContentRange]);
            Assert.NotEmpty(httpResponse.Headers[HeaderNames.LastModified]);
            Assert.Equal(contentLength, httpResponse.ContentLength);
        }

        [Fact]
        public async Task ExecuteResultAsync_SetsSuppliedContentTypeAndEncoding()
        {
            // Arrange
            var expectedContentType = "text/foo; charset=us-ascii";
            var path = Path.GetFullPath(Path.Combine(".", "TestFiles", "FilePathResultTestFile_ASCII.txt"));
            var result = new TestPhysicalFileResult(path, expectedContentType)
            {
                IsAscii = true
            };
            var httpContext = GetHttpContext();
            var memoryStream = new MemoryStream();
            httpContext.Response.Body = memoryStream;
            var context = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            // Act
            await result.ExecuteResultAsync(context);

            // Assert
            var contents = Encoding.ASCII.GetString(memoryStream.ToArray());
            Assert.Equal("FilePathResultTestFile contents ASCII encoded", contents);
            Assert.Equal(expectedContentType, httpContext.Response.ContentType);
        }

        [Fact]
        public async Task ExecuteResultAsync_WorksWithAbsolutePaths()
        {
            // Arrange
            var path = Path.GetFullPath(Path.Combine(".", "TestFiles", "FilePathResultTestFile.txt"));
            var result = new TestPhysicalFileResult(path, "text/plain");

            var httpContext = GetHttpContext();
            httpContext.Response.Body = new MemoryStream();

            var context = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            // Act
            await result.ExecuteResultAsync(context);
            httpContext.Response.Body.Position = 0;

            // Assert
            Assert.NotNull(httpContext.Response.Body);
            var contents = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
            Assert.Equal("FilePathResultTestFile contents�", contents);
        }

        [Theory]
        [InlineData("FilePathResultTestFile.txt")]
        [InlineData("./FilePathResultTestFile.txt")]
        [InlineData(".\\FilePathResultTestFile.txt")]
        [InlineData("~/FilePathResultTestFile.txt")]
        [InlineData("..\\TestFiles/FilePathResultTestFile.txt")]
        [InlineData("..\\TestFiles\\FilePathResultTestFile.txt")]
        [InlineData("..\\TestFiles/SubFolder/SubFolderTestFile.txt")]
        [InlineData("..\\TestFiles\\SubFolder\\SubFolderTestFile.txt")]
        [InlineData("..\\TestFiles/SubFolder\\SubFolderTestFile.txt")]
        [InlineData("..\\TestFiles\\SubFolder/SubFolderTestFile.txt")]
        [InlineData("~/SubFolder/SubFolderTestFile.txt")]
        [InlineData("~/SubFolder\\SubFolderTestFile.txt")]
        public async Task ExecuteAsync_ThrowsNotSupported_ForNonRootedPaths(string path)
        {
            // Arrange
            var result = new TestPhysicalFileResult(path, "text/plain");
            var context = new ActionContext(GetHttpContext(), new RouteData(), new ActionDescriptor());
            var expectedMessage = $"Path '{path}' was not rooted.";

            // Act
            var ex = await Assert.ThrowsAsync<NotSupportedException>(() => result.ExecuteResultAsync(context));

            // Assert
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Theory]
        [InlineData("/SubFolder/SubFolderTestFile.txt")]
        [InlineData("\\SubFolder\\SubFolderTestFile.txt")]
        [InlineData("/SubFolder\\SubFolderTestFile.txt")]
        [InlineData("\\SubFolder/SubFolderTestFile.txt")]
        [InlineData("./SubFolder/SubFolderTestFile.txt")]
        [InlineData(".\\SubFolder\\SubFolderTestFile.txt")]
        [InlineData("./SubFolder\\SubFolderTestFile.txt")]
        [InlineData(".\\SubFolder/SubFolderTestFile.txt")]
        public void ExecuteAsync_ThrowsDirectoryNotFound_IfItCanNotFindTheDirectory_ForRootPaths(string path)
        {
            // Arrange
            var result = new TestPhysicalFileResult(path, "text/plain");
            var context = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());

            // Act & Assert
            Assert.ThrowsAsync<DirectoryNotFoundException>(() => result.ExecuteResultAsync(context));
        }

        [Theory]
        [InlineData("/FilePathResultTestFile.txt")]
        [InlineData("\\FilePathResultTestFile.txt")]
        public void ExecuteAsync_ThrowsFileNotFound_WhenFileDoesNotExist_ForRootPaths(string path)
        {
            // Arrange
            var result = new TestPhysicalFileResult(path, "text/plain");
            var context = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());

            // Act & Assert
            Assert.ThrowsAsync<FileNotFoundException>(() => result.ExecuteResultAsync(context));
        }

        private class TestPhysicalFileResult : PhysicalFileResult
        {
            public TestPhysicalFileResult(string filePath, string contentType)
                : base(filePath, contentType)
            {
            }

            public override Task ExecuteResultAsync(ActionContext context)
            {
                var executor = context.HttpContext.RequestServices.GetRequiredService<TestPhysicalFileResultExecutor>();
                executor.IsAscii = IsAscii;
                return executor.ExecuteAsync(context, this);
            }

            public bool IsAscii { get; set; } = false;
        }

        private class TestPhysicalFileResultExecutor : PhysicalFileResultExecutor
        {
            public TestPhysicalFileResultExecutor(ILoggerFactory loggerFactory)
                : base(loggerFactory)
            {
            }

            public bool IsAscii { get; set; } = false;

            protected override Stream GetFileStream(string path)
            {
                if (IsAscii)
                {
                    return new MemoryStream(Encoding.ASCII.GetBytes("FilePathResultTestFile contents ASCII encoded"));
                }
                else
                {
                    return new MemoryStream(Encoding.UTF8.GetBytes("FilePathResultTestFile contents�"));
                }
            }

            protected override FileInfo GetFileInfo(string path)
            {
                var lastModified = DateTimeOffset.UtcNow;
                return new FileInfo
                {
                    Exists = true,
                    Length = 34,
                    LastModified = new DateTimeOffset(lastModified.Year, lastModified.Month, lastModified.Day, lastModified.Hour, lastModified.Minute, lastModified.Second, TimeSpan.FromSeconds(0))
                };
            }
        }

        private class TestSendFileFeature : IHttpSendFileFeature
        {
            public string name = null;
            public long offset = 0;
            public long? length = null;
            public CancellationToken token;

            public Task SendFileAsync(string path, long offset, long? length, CancellationToken cancellation)
            {
                this.name = path;
                this.offset = offset;
                this.length = length;
                this.token = cancellation;

                return Task.FromResult(0);
            }
        }

        private static IServiceCollection CreateServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<TestPhysicalFileResultExecutor>();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            return services;
        }

        private static HttpContext GetHttpContext()
        {
            var services = CreateServices();

            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = services.BuildServiceProvider();

            return httpContext;
        }
    }
}