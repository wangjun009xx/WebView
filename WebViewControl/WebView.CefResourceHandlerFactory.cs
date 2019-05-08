﻿using CefSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace WebViewControl {

    partial class WebView {

        protected class CefResourceHandlerFactory : IResourceHandlerFactory, ISchemeHandlerFactory, IDisposable {

            private readonly WebView OwnerWebView;

            private Dictionary<string, Assembly> assemblies;
            private bool newAssembliesLoaded = true;

            public CefResourceHandlerFactory(WebView webView) {
                OwnerWebView = webView;
            }

            public bool HasHandlers {
                get { return true; }
            }

            IResourceHandler ISchemeHandlerFactory.Create(IBrowser browser, IFrame frame, string schemeName, IRequest request) {
                return GetResourceHandler(request);
            }

            IResourceHandler IResourceHandlerFactory.GetResourceHandler(IWebBrowser browserControl, IBrowser browser, IFrame frame, IRequest request) {
                return GetResourceHandler(request);
            }

            private IResourceHandler GetResourceHandler(IRequest request) {
                if (request.Url == OwnerWebView.DefaultLocalUrl) {
                    return OwnerWebView.htmlToLoad != null ? ResourceHandler.FromString(OwnerWebView.htmlToLoad) : null;
                }

                if (OwnerWebView.FilterRequest(request)) {
                    return null;
                }

                var resourceHandler = new ResourceHandler(request, OwnerWebView.GetRequestUrl(request.Url, (ResourceType)request.ResourceType));
                HandleRequest(resourceHandler);
                return resourceHandler.Handler;
            }

            protected void HandleRequest(ResourceHandler resourceHandler) {
                if (Uri.TryCreate(resourceHandler.Url, UriKind.Absolute, out var url) && url.Scheme == ResourceUrl.EmbeddedScheme) {
                    var urlWithoutQuery = new UriBuilder(url);
                    if (url.Query != "") {
                        urlWithoutQuery.Query = "";
                    }
                    OwnerWebView.ExecuteWithAsyncErrorHandling(() => LoadEmbeddedResource(resourceHandler, urlWithoutQuery.Uri));
                }

                if (OwnerWebView.BeforeResourceLoad != null) {
                    OwnerWebView.ExecuteWithAsyncErrorHandling(() => OwnerWebView.BeforeResourceLoad(resourceHandler));
                }

                if (resourceHandler.Handled) {
                    return;
                }

                if (!OwnerWebView.IgnoreMissingResources && url != null && url.Scheme == ResourceUrl.EmbeddedScheme) {
                    if (OwnerWebView.ResourceLoadFailed != null) {
                        OwnerWebView.ResourceLoadFailed(resourceHandler.Url);
                    } else {
                        OwnerWebView.ExecuteWithAsyncErrorHandling(() => throw new InvalidOperationException("Resource not found: " + resourceHandler.Url));
                    }
                }
            }

            protected void LoadEmbeddedResource(ResourceHandler resourceHandler, Uri url) {
                var resourceAssembly = ResolveResourceAssembly(url);
                var resourcePath = ResourceUrl.GetEmbeddedResourcePath(url);

                var extension = Path.GetExtension(resourcePath.Last()).ToLower();

                var resourceStream = TryGetResourceWithFullPath(resourceAssembly, resourcePath);
                if (resourceStream != null) {
                    resourceHandler.RespondWith(resourceStream, extension);
                }
            }

            protected Assembly ResolveResourceAssembly(Uri resourceUrl) {
                if (assemblies == null) {
                    assemblies = new Dictionary<string, Assembly>();
                    AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
                }

                var assemblyName = ResourceUrl.GetEmbeddedResourceAssemblyName(resourceUrl);
                var assembly = GetAssemblyByName(assemblyName);

                if (assembly == null) {
                    if (newAssembliesLoaded) {
                        // add loaded assemblies to cache
                        newAssembliesLoaded = false;
                        foreach (var domainAssembly in AppDomain.CurrentDomain.GetAssemblies()) {
                            // replace if duplicated (can happen)
                            assemblies[domainAssembly.GetName().Name] = domainAssembly;
                        }
                    }

                    assembly = GetAssemblyByName(assemblyName);
                    if (assembly == null) {
                        // try load assembly from its name
                        assembly = AppDomain.CurrentDomain.Load(new AssemblyName(assemblyName));
                        if (assembly != null) {
                            assemblies[assembly.GetName().Name] = assembly;
                        }
                    }
                }

                if (assembly != null) {
                    return assembly;
                }

                throw new InvalidOperationException("Could not find assembly for: " + resourceUrl);
            }

            private Assembly GetAssemblyByName(string assemblyName) {
                Assembly assembly;
                assemblies.TryGetValue(assemblyName, out assembly);
                return assembly;
            }

            protected Stream TryGetResourceWithFullPath(Assembly assembly, IEnumerable<string> resourcePath) {
                return ResourcesManager.TryGetResourceWithFullPath(assembly, resourcePath);
            }

            private void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args) {
                newAssembliesLoaded = true;
            }

            public void Dispose() {
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
            }
        }
    }
}
