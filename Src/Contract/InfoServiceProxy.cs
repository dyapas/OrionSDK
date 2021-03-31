using System;
using System.Diagnostics;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace SolarWinds.InformationService.Contract2
{
    public class InfoServiceProxy : IStreamInformationService, IDisposable
    {
        private readonly ILogger<InfoServiceProxy> _log;
        private static readonly TimeSpan longRunningQueryTime = TimeSpan.FromSeconds(15);

        private IStreamInformationServiceChannel _infoService;

        private InfoServiceActivityMonitor _activityMonitor = null;

        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(60);

        public IChannel Channel
        {
            get { return _infoService; }
        }

        public IClientChannel ClientChannel
        {
            get
            {
                return _infoService;
            }
        }

        public ChannelFactory<IStreamInformationServiceChannel> ChannelFactory { get; private set; }

        #region Constructors

        public InfoServiceProxy(ILogger<InfoServiceProxy> logger, string endpointConfiguration)
        {
            _log = logger ?? throw new ArgumentNullException(nameof(logger));

            if (endpointConfiguration == null)
                throw new ArgumentNullException(nameof(endpointConfiguration));

            ChannelFactory = CreateChannelFactory(endpointConfiguration);

            FixBinding();
        }

        public InfoServiceProxy(ILogger<InfoServiceProxy> logger, string endpointConfiguration, ServiceCredentials credentials)
            : this(logger, endpointConfiguration)
        {
            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));

            credentials.ApplyTo(ChannelFactory);
        }

        public InfoServiceProxy(ILogger<InfoServiceProxy> logger, string endpointConfiguration, string remoteAddress)
        {
            _log = logger ?? throw new ArgumentNullException(nameof(logger));

            if (endpointConfiguration == null)
                throw new ArgumentNullException(nameof(endpointConfiguration));

            if (remoteAddress == null)
                throw new ArgumentNullException(nameof(remoteAddress));

            ChannelFactory = CreateChannelFactory(endpointConfiguration, new EndpointAddress(remoteAddress));

            FixBinding();
        }

        public InfoServiceProxy(ILogger<InfoServiceProxy> logger, string endpointConfiguration, string remoteAddress, ServiceCredentials credentials)
            : this(logger, endpointConfiguration, remoteAddress)
        {
            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));

            credentials.ApplyTo(ChannelFactory);
        }

        public InfoServiceProxy(ILogger<InfoServiceProxy> logger, Uri address, Binding binding, ServiceCredentials credentials)
        {
            _log = logger ?? throw new ArgumentNullException(nameof(logger));

            if (address == null)
                throw new ArgumentNullException(nameof(address));

            var endpoint = new EndpointAddress(address, new DnsEndpointIdentity("SolarWinds-Orion"));
            Initialize(endpoint, binding, credentials);
            ChannelFactory.Endpoint.Address = endpoint; // for some reason this gets lost and needs to be set again after creation
        }

        #endregion

        private void FixBinding()
        {
            BindingElementCollection elements = ChannelFactory.Endpoint.Binding.CreateBindingElements();
            SslStreamSecurityBindingElement element = elements.Find<SslStreamSecurityBindingElement>();
            if (element != null)
            {
                CustomBinding newbinding = new CustomBinding(elements);

                // Transfer timeout settings from the old binding to the new
                Binding binding = ChannelFactory.Endpoint.Binding;
                newbinding.CloseTimeout = binding.CloseTimeout;
                newbinding.OpenTimeout = binding.OpenTimeout;
                newbinding.ReceiveTimeout = binding.ReceiveTimeout;
                newbinding.SendTimeout = binding.SendTimeout;

                ChannelFactory.Endpoint.Binding = newbinding;
            }

            CorrectChannelFactory();
        }

        private void Initialize(EndpointAddress address, Binding binding, ServiceCredentials credentials)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));

            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));

            if (binding == null)
                throw new ArgumentNullException(nameof(binding));


            BindingElementCollection elements = binding.CreateBindingElements();
            SslStreamSecurityBindingElement element = elements.Find<SslStreamSecurityBindingElement>();
            if (element != null)
            {
                CustomBinding newbinding = new CustomBinding(elements);

                // Transfer timeout settings from the old binding to the new
                newbinding.CloseTimeout = binding.CloseTimeout;
                newbinding.OpenTimeout = binding.OpenTimeout;
                newbinding.ReceiveTimeout = binding.ReceiveTimeout;
                newbinding.SendTimeout = binding.SendTimeout;
                binding = newbinding;
            }

            ChannelFactory = CreateChannelFactory(binding, address);
            credentials.ApplyTo(ChannelFactory);

            CorrectChannelFactory();
        }

        private void CorrectChannelFactory()
        {
            // ???: how can I detect that channel binding is securited            

            _activityMonitor = new InfoServiceActivityMonitor();
            ChannelFactory.Endpoint.EndpointBehaviors.Add(new InfoServiceDefaultBehaviour());
            ChannelFactory.Endpoint.EndpointBehaviors.Add(_activityMonitor);
        }

        #region IInfoService Members

        public virtual XmlElement Invoke(string entity, string verb, params XmlElement[] parameters)
        {
            var stopwatch = new Stopwatch();
            try
            {
                if (_infoService == null)
                {
                    Open();
                }
                stopwatch.Start();
                return _infoService.Invoke(entity, verb, parameters);
            }
            catch (FaultException<InfoServiceFaultContract> ex)
            {
                _log.LogError(ex, "Error executing invoke on {entity}.{verb}", entity, verb);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.Elapsed > longRunningQueryTime)
                {
                    _log.LogWarning("Support! -- LONG RUNNING INVOKE: {elapsed} ms: {verb}:{entity}", stopwatch.Elapsed.TotalMilliseconds, verb, entity);
                }
            }
        }

        public virtual Message Query(QueryXmlRequest query)
        {
            _log.LogDebug("Query: {query}", query.query);
            if (_log.IsEnabled(LogLevel.Debug) && query.parameters.Count > 0)
            {
                _log.LogDebug("Parameters: ");
                foreach (var parameter in query.parameters)
                    _log.LogDebug("\t{parameter}={value}", parameter.Key, parameter.Value);
            }
            var stopwatch = new Stopwatch();
            try
            {
                if (_infoService == null)
                {
                    Open();
                }
                stopwatch.Start();
                return _infoService.Query(query);
            }
            catch (FaultException<InfoServiceFaultContract> ex)
            {
                _log.LogError(ex, "Error executing query:" + Environment.NewLine + "{query}", query.query);
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error executing query:" + Environment.NewLine + "{query}", query.query);
                foreach (var parameter in query.parameters)
                    _log.LogError("\t{parameter}={value}", parameter.Key, parameter.Value);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.Elapsed > longRunningQueryTime)
                {
                    _log.LogWarning("Support! -- LONG RUNNING QUERY: {elapsed} ms: {query}", stopwatch.Elapsed.TotalMilliseconds, query.query);
                }
            }
        }

        public virtual string Create(string entityType, PropertyBag properties)
        {
            var stopwatch = new Stopwatch();
            try
            {
                if (_infoService == null)
                {
                    Open();
                }
                stopwatch.Start();
                return _infoService.Create(entityType, properties);
            }
            catch (FaultException<InfoServiceFaultContract> ex)
            {
                _log.LogError(ex, "Error executing create operation: " + Environment.NewLine + entityType + Environment.NewLine + properties);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.Elapsed > longRunningQueryTime)
                {
                    _log.LogWarning("Support! -- LONG RUNNING CREATE: {elapsed} ms: {entityType}", stopwatch.Elapsed.TotalMilliseconds, entityType);
                }
            }
        }

        public virtual PropertyBag Read(string uri)
        {
            var stopwatch = new Stopwatch();
            try
            {
                if (_infoService == null)
                {
                    Open();
                }
                stopwatch.Start();
                return _infoService.Read(uri);
            }
            catch (FaultException<InfoServiceFaultContract> ex)
            {
                _log.LogError(ex, "Error executing read operation: {uri}", uri);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.Elapsed > longRunningQueryTime)
                {
                    _log.LogWarning("Support! -- LONG RUNNING READ: {elapsed} ms: {uri}", stopwatch.Elapsed.TotalMilliseconds, uri);
                }
            }
        }

        public virtual void Update(string uri, PropertyBag propertiesToUpdate)
        {
            var stopwatch = new Stopwatch();
            try
            {
                if (_infoService == null)
                {
                    Open();
                }
                stopwatch.Start();
                _infoService.Update(uri, propertiesToUpdate);
            }
            catch (FaultException<InfoServiceFaultContract> ex)
            {
                _log.LogError(ex, "Error executing update operation: " + uri + Environment.NewLine + propertiesToUpdate);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.Elapsed > longRunningQueryTime)
                {
                    _log.LogWarning("Support! -- LONG RUNNING UPDATE: {elapsed} ms: {uri}", stopwatch.Elapsed.TotalMilliseconds, uri);
                }
            }
        }

        public virtual void BulkUpdate(string[] uris, PropertyBag propertiesToUpdate)
        {
            var stopwatch = new Stopwatch();
            try
            {
                if (_infoService == null)
                {
                    Open();
                }
                stopwatch.Start();
                _infoService.BulkUpdate(uris, propertiesToUpdate);
            }
            catch (FaultException<InfoServiceFaultContract> ex)
            {
                _log.LogError(ex, "Error executing bulk update operation:" + Environment.NewLine + string.Join(Environment.NewLine, uris) + Environment.NewLine + propertiesToUpdate);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.Elapsed > longRunningQueryTime)
                {
                    _log.LogWarning("Support! -- LONG RUNNING BULK UPDATE: {elapsed} ms", stopwatch.Elapsed.TotalMilliseconds);
                }
            }
        }

        public virtual void Delete(string uri)
        {
            var stopwatch = new Stopwatch();
            try
            {
                if (_infoService == null)
                {
                    Open();
                }
                stopwatch.Start();
                _infoService.Delete(uri);
            }
            catch (FaultException<InfoServiceFaultContract> ex)
            {
                _log.LogError(ex, "Error executing delete operation: {uri}", uri);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.Elapsed > longRunningQueryTime)
                {
                    _log.LogWarning("Support! -- LONG RUNNING DELETE: {elapsed} ms: {uri}", stopwatch.Elapsed.TotalMilliseconds, uri);
                }
            }
        }

        public virtual void BulkDelete(string[] uris)
        {
            var stopwatch = new Stopwatch();
            try
            {
                if (_infoService == null)
                {
                    Open();
                }
                stopwatch.Start();
                _infoService.BulkDelete(uris);
            }
            catch (FaultException<InfoServiceFaultContract> ex)
            {
                _log.LogError(ex, "Error executing bulk delete operation: " + Environment.NewLine + string.Join(Environment.NewLine, uris));
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.Elapsed > longRunningQueryTime)
                {
                    _log.LogWarning("Support! -- LONG RUNNING BULK DELETE: {elapsed} ms", stopwatch.Elapsed.TotalMilliseconds);
                }
            }
        }

        #endregion

        #region IStreamedInfoService Members

        public VerbInvokeResponse StreamedInvoke(VerbInvokeArguments parameter)
        {
            var stopwatch = new Stopwatch();
            try
            {
                if (_infoService == null)
                {
                    Open();
                }
                stopwatch.Start();
                return _infoService.StreamedInvoke(parameter);
            }
            catch (FaultException<InfoServiceFaultContract> ex)
            {
                _log.LogError(ex, "Error executing invoke: {entity}.{verb}", parameter.Entity, parameter.Verb);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.Elapsed > longRunningQueryTime)
                {
                    _log.LogWarning("Support! -- LONG RUNNING INVOKE: {elapsed} ms: {verb}:{entity}", stopwatch.Elapsed.TotalMilliseconds, parameter.Verb, parameter.Entity);
                }
            }
        }

        #endregion

        public void Open()
        {
            try
            {
                if (_infoService == null)
                {
                    if (_activityMonitor != null)
                        _activityMonitor.Reset();

                    _infoService = ChannelFactory.CreateChannel();

                    _infoService.OperationTimeout = OperationTimeout;
                    _infoService.Open();
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "An error occurred opening a connection to the Orion communication service.");
                throw;
            }
        }

        public void Abort()
        {
            if (_infoService == null)
                return;

            ValidateUsedConnection();

            _infoService.Abort();
            ChannelFactory.Abort();
        }

        public void Close()
        {
            if (_infoService == null)
                return;

            ValidateUsedConnection();

            try
            {
                _infoService.Close();
            }
            catch (TimeoutException exception)
            {
                _infoService.Abort();
                _log.LogError(exception, "Error closing exception.");
            }
            catch (CommunicationException exception)
            {
                _infoService.Abort();
                _log.LogError(exception, "Error closing exception.");
            }

            _infoService = null;
        }

        #region Create Channel Factory

        private ChannelFactory<IStreamInformationServiceChannel> CreateChannelFactory(Binding binding, EndpointAddress address)
        {
            if (_log.IsEnabled(LogLevel.Debug))
                _log.LogDebug("Creating channel factory for Information Service @ {uri}", address.Uri);

            return new ChannelFactory<IStreamInformationServiceChannel>(binding, address);
        }

        private ChannelFactory<IStreamInformationServiceChannel> CreateChannelFactory(string endpointConfiguration)
        {
            if (_log.IsEnabled(LogLevel.Debug))
                _log.LogDebug($"Creating channel factory for Information Service using endpoint configuration '{endpointConfiguration}'", endpointConfiguration);

            return new ChannelFactory<IStreamInformationServiceChannel>(endpointConfiguration);
        }

        private ChannelFactory<IStreamInformationServiceChannel> CreateChannelFactory(string endpointConfiguration, EndpointAddress remoteAddress)
        {
            if (_log.IsEnabled(LogLevel.Debug))
                _log.LogDebug("Creating channel factory for Information Service using endpoint configuration '{endpointConfiguration}' and remote address '{remoteAddress}'", endpointConfiguration, remoteAddress.ToString());

            return new ChannelFactory<IStreamInformationServiceChannel>(endpointConfiguration, remoteAddress);
        }

        #endregion

        private void ValidateUsedConnection()
        {
            if (_infoService == null)
                return;

            if (_activityMonitor == null || _activityMonitor.RequestSent)
                return;

            _log.LogInformation("Non-used connection was opened. Information for developers. No impact on product functionality. See verbose log for more details.");
            _log.LogTrace("StackTrace: {stackTrace}", Environment.StackTrace);

            try
            {
                //
                // kick of simple query because of 
                // https://connect.microsoft.com/VisualStudio/feedback/details/499859/wcf-pending-secure-conversions-are-not-cleaned-up-in-specific-scenario
                //                    
                using (_infoService.Query(new QueryXmlRequest("SELECT TOP 1 1 as Test FROM Metadata.Entity")))
                {

                }
            }
            catch { }
        }

        #region IDisposable Members

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Close();

                try
                {
                    ChannelFactory.Close();
                }
                catch (TimeoutException)
                {
                    ChannelFactory.Abort();
                }
                catch (CommunicationException)
                {
                    ChannelFactory.Abort();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~InfoServiceProxy()
        {
            Dispose(false);
        }

        #endregion
    }
}
