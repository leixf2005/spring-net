#region License

/*
 * Copyright � 2002-2005 the original author or authors.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#endregion

#region Imports

using System;
using System.Collections;
using System.IO;
using System.Web;
using Common.Logging;
using Spring.Collections;
using Spring.Context;
using Spring.Context.Support;
using Spring.Objects.Factory.Config;
using Spring.Util;

#endregion

namespace Spring.Web.Support
{
    /// <summary>
    /// Provides base functionality for Spring.NET context-aware
    /// <see cref="System.Web.IHttpHandlerFactory"/> implementations.
    /// </summary>
    /// <remarks>
    /// <p>
    /// Provides derived classes with a default implementation of
    /// <see cref="IHttpHandlerFactory.ReleaseHandler(IHttpHandler)"/> method.
    /// </p>
    /// </remarks>
    /// <author>Aleksandar Seovic</author>
    public abstract class AbstractHandlerFactory : IHttpHandlerFactory
    {
        /// <summary>
        /// Holds all handlers having <see cref="IHttpHandler.IsReusable"/> == true.
        /// </summary>
        private readonly IDictionary _reusableHandlerCache = new CaseInsensitiveHashtable();

        /// <summary>
        /// Holds the shared logger for all factories.
        /// </summary>
        protected readonly ILog Log;

        /// <summary>
        /// Creates a new instance of the
        /// <see cref="Spring.Web.Support.AbstractHandlerFactory"/> class.
        /// </summary>
        protected AbstractHandlerFactory()
        {
            this.Log = LogManager.GetLogger( this.GetType() );
        }

        /// <summary>
        /// Returns an appropriate <see cref="System.Web.IHttpHandler"/> implementation.
        /// </summary>
        /// <param name="context">
        /// An instance of the <see cref="System.Web.HttpContext"/> class that
        /// provides references to intrinsic server objects.
        /// </param>
        /// <param name="requestType">
        /// The HTTP method of the request.
        /// </param>
        /// <param name="url">The request URL.</param>
        /// <param name="physicalPath">
        /// The physical path of the requested resource.
        /// </param>
        /// <returns>
        /// A new <see cref="System.Web.IHttpHandler"/> object that processes
        /// the request.
        /// </returns>
        public virtual IHttpHandler GetHandler( HttpContext context, string requestType, string url, string physicalPath )
        {
            bool isDebug = Log.IsDebugEnabled;

            #region Instrumentation

            if (isDebug)
                Log.Debug( string.Format( "GetHandler():resolving url '{0}'", url ) );

            #endregion

            IHttpHandler handler = null;
            lock (_reusableHandlerCache.SyncRoot)
            {
                handler = (IHttpHandler)_reusableHandlerCache[url];
            }

            if (handler != null)
            {
                #region Instrumentation

                if (isDebug)
                {
                    Log.Debug( string.Format( "GetHandler():resolved url '{0}' from reusable handler cache", url ) );
                }

                #endregion

                return handler;
            }

            lock (_reusableHandlerCache.SyncRoot)
            {
                handler = (IHttpHandler)_reusableHandlerCache[url];
                if (handler == null)
                {
                    handler = CreateHandlerInstance( context, requestType, url, physicalPath );

                    if (handler.IsReusable)
                    {
                        _reusableHandlerCache[url] = handler;
                    }
                }
                return handler;
            }
        }

        /// <summary>
        /// Enables a factory to release an existing
        /// <see cref="System.Web.IHttpHandler"/> instance.
        /// </summary>
        /// <param name="handler">
        /// The <see cref="System.Web.IHttpHandler"/> object to release.
        /// </param>
        public virtual void ReleaseHandler( IHttpHandler handler )
        { }

        /// <summary>
        /// Create a handler instance for the given URL.
        /// </summary>
        /// <param name="context">The <see cref="HttpContext"/> instance for this request.</param>
        /// <param name="requestType">The HTTP data transfer method (GET, POST, ...)</param>
        /// <param name="url">The requested <see cref="HttpRequest.RawUrl"/>.</param>
        /// <param name="physicalPath">The physical path of the requested resource.</param>
        /// <returns>A handler instance for processing the current request.</returns>
        protected abstract IHttpHandler CreateHandlerInstance( HttpContext context, string requestType, string url, string physicalPath );

        /// <summary>
        /// Get the application context instance corresponding to the given absolute url and checks 
        /// it for <see cref="IConfigurableApplicationContext"/> contract and being not null.
        /// </summary>
        /// <param name="url">the absolute url</param>
        /// <exception cref="ArgumentException">
        /// if no context is found
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// if context is not an <see cref="IConfigurableApplicationContext"/>
        /// </exception>
        /// <returns>teh application context instance corresponding to the given absolute url.</returns>
        /// <remarks>
        /// Calls <see cref="GetContext"/> to obtain a context instance.
        /// </remarks>
        protected IConfigurableApplicationContext GetCheckedApplicationContext( string url )
        {
            IApplicationContext appContext = GetContext( url );
            if (appContext == null)
            {
                throw new ArgumentException( string.Format( "no application context for virtual path '{0}'", url ) );
            }
            if (!(appContext is IConfigurableApplicationContext))
            {
                throw new InvalidOperationException( string.Format( "application context '{0}' for virtual path '{1}' must implement IConfigurableApplicationContext", appContext.ToString(), url ) );
            }
            return (IConfigurableApplicationContext)appContext;
        }

        /// <summary>
        /// Returns the unchecked, raw application context for the given virtual path.
        /// </summary>
        /// <param name="virtualPath">the virtual path to get the context for.</param>
        /// <returns>the context or null.</returns>
        /// <remarks>
        /// Subclasses may override this method to change the context source. 
        /// By default, <see cref="WebApplicationContext.GetContext"/> is used for obtaining context instances.
        /// </remarks>
        protected virtual IApplicationContext GetContext( string virtualPath )
        {
            return WebApplicationContext.GetContext( virtualPath );
        }

        /// <summary>
        /// DO NOT USE - this is subject to change!
        /// </summary>
        /// <param name="appRelativeVirtualPath"></param>
        /// <param name="objectFactory"></param>
        /// <returns>
        /// This method requires registrars to follow the convention of registering web object definitions using their
        /// application relative urls (~/mypath/mypage.aspx). 
        /// </returns>
        /// <remarks>
        /// Resolve an object definition by url.
        /// </remarks>
        protected internal static NamedObjectDefinition FindWebObjectDefinition( string appRelativeVirtualPath, IConfigurableListableObjectFactory objectFactory )
        {
            ILog Log = LogManager.GetLogger( typeof( AbstractHandlerFactory ) );
            bool isDebug = Log.IsDebugEnabled;

            // lookup definition using app-relative url
            if (isDebug)
                Log.Debug( string.Format( "GetHandler():looking up definition for app-relative url '{0}'", appRelativeVirtualPath ) );
            string objectDefinitionName = appRelativeVirtualPath;
            IObjectDefinition pageDefinition = objectFactory.GetObjectDefinition( appRelativeVirtualPath, true );

            if (pageDefinition == null)
            {
                // try using pagename+extension and pagename only
                string pageExtension = Path.GetExtension( appRelativeVirtualPath );
                string pageName = WebUtils.GetPageName( appRelativeVirtualPath );
                // only looks in the specified object factory -- it will *not* search parent contexts
                pageDefinition = objectFactory.GetObjectDefinition( pageName + pageExtension, false );
                if (pageDefinition == null)
                {
                    pageDefinition = objectFactory.GetObjectDefinition( pageName, false );
                    if (pageDefinition != null)
                        objectDefinitionName = pageName;
                }
                else
                {
                    objectDefinitionName = pageName + pageExtension;
                }

                if (pageDefinition != null)
                {
                    if (isDebug)
                        Log.Debug( string.Format( "GetHandler():found definition for page-name '{0}'", objectDefinitionName ) );
                }
                else
                {
                    if (isDebug)
                        Log.Debug( string.Format( "GetHandler():no definition found for page-name '{0}'", pageName ) );
                }
            }
            else
            {
                if (isDebug)
                    Log.Debug( string.Format( "GetHandler():found definition for page-url '{0}'", appRelativeVirtualPath ) );
            }

            return (pageDefinition == null) ? (NamedObjectDefinition)null : new NamedObjectDefinition( objectDefinitionName, pageDefinition );
        }

        /// <summary>
        /// DO NOT USE - this is subject to change!
        /// </summary>
        protected internal class NamedObjectDefinition
        {
            private readonly string _name;
            private readonly IObjectDefinition _objectDefinition;

            /// <summary>
            /// DO NOT USE
            /// </summary>
            public NamedObjectDefinition( string name, IObjectDefinition objectDefinition )
            {
                _name = name;
                _objectDefinition = objectDefinition;
            }

            /// <summary>
            /// DO NOT USE
            /// </summary>
            public string Name
            {
                get { return _name; }
            }

            /// <summary>
            /// DO NOT USE
            /// </summary>
            public IObjectDefinition ObjectDefinition
            {
                get { return _objectDefinition; }
            }
        }
    }
}