using Masterloop.Core.Types.Base;
using Masterloop.Core.Types.Commands;
using Masterloop.Core.Types.LiveConnect;
using Masterloop.Core.Types.Observations;
using Masterloop.Core.Types.Pulse;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Text;

namespace Masterloop.Plugin.Device
{
    /// <summary>
    /// Masterloop Cloud Services Live Device Plugin
    /// </summary>
    public class LiveDevice : DeviceBase
    {
        #region Constants
        private ConnectionFactory _connectionFactory;
        private IConnection _connection;
        private IModel _model;
        private EventingBasicConsumer _consumer;
        private string _consumerTag;
        private readonly string _rmqExchangeName;
        private readonly string _rmqQueueName;
        private List<CommandSubscription<Command>> _commandSubscriptions;
        private List<PulseSubscription> _pulseSubscriptions;
        private ushort _heartbeatInterval;
        private bool _disposed;
        private bool _transactionOpen;
        private const string _addressDeviceConnect = "/api/devices/{0}/connect";
        private readonly object _modelLock;
        #endregion // Constants

        #region Properties
        /// <summary>
        /// True ignores any SSL certificate errors, False does not ignore any SSL certificate errors. Default false.
        /// </summary>
        public bool IgnoreSslCertificateErrors { get; set; }

        /// <summary>
        /// Specifies the requested heartbeat interval in seconds. Must be within the range of [60, 3600] seconds. Use 0 to disable heartbeats.
        /// More info can be found here: https://www.rabbitmq.com/heartbeats.html
        /// Default 60 seconds.
        /// </summary>
        public ushort HeartbeatInterval
        {
            get
            {
                return _heartbeatInterval;
            }
            set
            {
                if (value == 0 || (value >= 60 && value <= 3600))
                {
                    _heartbeatInterval = value;
                }
                else
                {
                    throw new ArgumentOutOfRangeException("HeartbeatInterval", value, "Heartbeat interval must be between 60 and 3600 seconds (or 0 for disabled).");
                }
            }
        }

        /// <summary>
        /// Flag indicating if callbacks should be fired immediatelly (true) or when Fetch is explicitly called (false). Default is true.
        /// </summary>
        public bool UseAutomaticCallbacks { get; set; }

        /// <summary>
        /// Flag indicating use of atomic / one-by-one message mode (true) or begin/add/commit-rollback transaction communication (false). Default true.
        /// </summary>
        public bool UseAtomicTransactions { get; set; }

        /// <summary>
        /// Prefetch count indicating number of messages in-flight between publisher and subscriber. Default 20.
        /// </summary>
        public int PrefetchCount { get; set; } = 20;

        /// <summary>
        /// Interval to back off/wait in case connection to broker fails. Set by server upon initial connection. Default 1 minute.
        /// </summary>
        public TimeSpan BackOffInterval { get; private set; }
        #endregion

        #region Construction
        /// <summary>
        /// Constructs a new LiveDevice object.
        /// </summary>
        /// <param name="MID">Device identifier.</param>
        /// <param name="preSharedKey">Pre shared key for device.</param>
        /// <param name="hostName">MCS host to connect to, e.g. "api.masterloop.net" or "10.0.0.2".</param>
        /// <param name="useHttps">True if using HTTPS (SSL/TLS), False if using HTTP (unencrypted).</param>
        /// <param name="port">Optional overriding of port number for HTTP(S) communication.</param>
        public LiveDevice(string MID, string preSharedKey, string hostName, bool useHttps = true, ushort? port = null)
            : base(MID, preSharedKey, hostName, useHttps, port)
        {
            _modelLock = new object();
            _disposed = false;
            _rmqExchangeName = string.Format("{0}.X", MID);
            _rmqQueueName = string.Format("{0}.Q", _MID);
            _commandSubscriptions = new List<CommandSubscription<Command>>();
            _pulseSubscriptions = new List<PulseSubscription>();
            _consumerTag = string.Empty;
            _transactionOpen = false;
            IgnoreSslCertificateErrors = false;
            HeartbeatInterval = 60;
            Timeout = 30;
            UseAutomaticCallbacks = true;
            UseAtomicTransactions = true;
            BackOffInterval = new TimeSpan(0, 1, 0);  // Default
        }

        /// <summary>
        /// Closes connection to MCS Live Server, and destroys current object.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                Disconnect();       
            }
            _disposed = true;
            base.Dispose(disposing);
        }
        #endregion

        #region Connection
        /// <summary>
        /// Connects to MCS Live Server.
        /// </summary>
        public bool Connect()
        {
            Disconnect();  // Remove any existing connection objects
            string received;
            string url = string.Format(_addressDeviceConnect, _MID);
            if (Get(url, out received))
            {
                if (received != null && received.Length > 0)
                {
                    DeviceConnection connectionDetails = JsonConvert.DeserializeObject<DeviceConnection>(received);
                    if (connectionDetails != null && connectionDetails.Node != null)
                    {
                        if (connectionDetails.BackoffSeconds > 0)
                        {
                            this.BackOffInterval = new TimeSpan(0, 0, connectionDetails.BackoffSeconds);
                        }

                        // Connect to messaging server.
                        if (Open(connectionDetails))
                        {
                            // Enable automatic callback handler if specified.
                            if (UseAutomaticCallbacks)
                            {
                                lock (_modelLock)
                                {
                                    _consumer = new EventingBasicConsumer(_model);
                                    _consumer.Received += ConsumerReceived;
                                    _consumerTag = _model.BasicConsume(_rmqQueueName, false, _consumer);
                                }
                            }
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Disconnects from MCS Live Server. 
        /// </summary>
        public void Disconnect()
        {
            if (_consumer != null)
            {
                lock (_consumer)
                {
                    _consumer.Received -= ConsumerReceived;
                    _consumer = null;
                }
            }

            if (_model != null)
            {
                lock (_modelLock)
                {
                    if (_model.IsOpen)
                    {
                        _model.Close(200, "Goodbye");
                    }
                    _model.Dispose();
                    _model = null;
                    _transactionOpen = false;
                }
            }

            if (_connection != null)
            {
                lock (_connection)
                {
                    if (_connection.IsOpen)
                    {
                        _connection.Close();
                    }
                    _connection.Dispose();
                    _connection = null;
                }
            }

            if (_connectionFactory != null)
            {
                lock (_connectionFactory)
                {
                    _connectionFactory = null;
                }
            }
        }

        /// <summary>
        /// Reports connection status to MCS Live Server.
        /// </summary>
        /// <returns>True if connected, False otherwise.</returns>
        public bool IsConnected()
        {
            if (_connectionFactory == null) return false;
            if (_connection == null) return false;
            if (!_connection.IsOpen) return false;
            lock (_modelLock)
            {
                if (_model == null) return false;
                return _model.IsOpen;
            }
        }

        /// <summary>
        /// Pauses listening for incoming messages.
        /// </summary>
        /// <returns></returns>
        public bool PauseIncoming()
        {
            bool success = false;
            if (UseAutomaticCallbacks && _consumer != null && _consumerTag != string.Empty)
            {
                lock (_model)
                {
                    try
                    {
                        _consumer.Received -= ConsumerReceived;
                        _model.BasicCancel(_consumerTag);
                        _consumerTag = string.Empty;
                        success = true;
                    }
                    catch (Exception e)
                    {
                        LastErrorMessage = e.Message;
                    }
                }
            }
            return success;
        }

        /// <summary>
        /// Resumes listening for incoming messages.
        /// </summary>
        /// <returns></returns>
        public bool ResumeIncoming()
        {
            bool success = false;
            if (UseAutomaticCallbacks && _consumer != null && _consumerTag != string.Empty)
            {
                lock (_model)
                {
                    try
                    {
                        _consumer.Received += ConsumerReceived;
                        _consumerTag = _model.BasicConsume(_rmqQueueName, false, _consumer);
                        success = true;
                    }
                    catch (Exception e)
                    {
                        LastErrorMessage = e.Message;
                    }
                }
            }
            return success;
        }
        #endregion

        #region Messaging
        /// <summary>
        /// Publishes a binary/blob observation to the server. 
        /// </summary>
        /// <param name="observationId">Observation identifier.</param>
        /// <param name="value">Observation byte array value.</param>
        /// <returns>True if successful, False otherwise.</returns>
        public bool PublishObservation(int observationId, byte[] value)
        {
            return PublishObservationMessage(observationId, value);
        }

        /// <summary>
        /// Publishes a new time-bound observation value of type 'Boolean' to the server. 
        /// </summary>
        /// <param name="observationId">Observation identifier.</param>
        /// <param name="timestamp">Date and time in UTC.</param>
        /// <param name="value">Observation value.</param>
        /// <returns>True if successful, False otherwise.</returns>
        public bool PublishObservation(int observationId, DateTime timestamp, bool value)
        {
            BooleanObservation o = new BooleanObservation()
            {
                Timestamp = timestamp,
                Value = value
            };
            string json = JsonConvert.SerializeObject(o);
            return PublishObservationMessage(observationId, Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Publishes a new time-bound observation value of type 'Double' to the server. 
        /// </summary>
        /// <param name="observationId">Observation identifier.</param>
        /// <param name="timestamp">Date and time in UTC.</param>
        /// <param name="value">Observation value.</param>
        /// <returns>True if successful, False otherwise.</returns>
        public bool PublishObservation(int observationId, DateTime timestamp, double value)
        {
            DoubleObservation o = new DoubleObservation()
            {
                Timestamp = timestamp,
                Value = value
            };
            string json = JsonConvert.SerializeObject(o);
            return PublishObservationMessage(observationId, Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Publishes a new time-bound observation value of type 'Integer' to the server. 
        /// </summary>
        /// <param name="observationId">Observation identifier.</param>
        /// <param name="timestamp">Date and time in UTC.</param>
        /// <param name="value">Observation value.</param>
        /// <returns>True if successful, False otherwise.</returns>
        public bool PublishObservation(int observationId, DateTime timestamp, int value)
        {
            IntegerObservation o = new IntegerObservation()
            {
                Timestamp = timestamp,
                Value = value
            };
            string json = JsonConvert.SerializeObject(o);
            return PublishObservationMessage(observationId, Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Publishes a new time-bound observation value of type 'Position' to the server.
        /// </summary>
        /// <param name="observationId">Observation identifier.</param>
        /// <param name="timestamp">Date and time in UTC.</param>
        /// <param name="value">Position coordinates according with reference to WGS84.</param>
        /// <returns>True if successful, False otherwise.</returns>
        public bool PublishObservation(int observationId, DateTime timestamp, Position value)
        {
            PositionObservation o = new PositionObservation()
            {
                Timestamp = timestamp,
                Value = value
            };
            string json = JsonConvert.SerializeObject(o);
            return PublishObservationMessage(observationId, Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Publishes a new time-bound observation value of type 'String' to the server. 
        /// </summary>
        /// <param name="observationId">Observation identifier.</param>
        /// <param name="timestamp">Date and time in UTC.</param>
        /// <param name="value">Observation value.</param>
        /// <returns>True if successful, False otherwise.</returns>
        public bool PublishObservation(int observationId, DateTime timestamp, string value)
        {
            StringObservation o = new StringObservation()
            {
                Timestamp = timestamp,
                Value = value
            };
            string json = JsonConvert.SerializeObject(o);
            return PublishObservationMessage(observationId, Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Publishes a new time-bound observation value of type 'Statistics' to the server. 
        /// </summary>
        /// <param name="observationId">Observation identifier.</param>
        /// <param name="timestamp">Date and time in UTC.</param>
        /// <param name="value">Observation value.</param>
        /// <returns>True if successful, False otherwise.</returns>
        public bool PublishObservation(int observationId, DateTime timestamp, DescriptiveStatistics value)
        {
            StatisticsObservation o = new StatisticsObservation()
            {
                Timestamp = timestamp,
                Value = value
            };
            string json = JsonConvert.SerializeObject(o);
            return PublishObservationMessage(observationId, Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Begins a new multi publish transaction.
        /// </summary>
        /// <returns>True if successful, False otherwise.</returns>
        public bool PublishBegin()
        {
            if (UseAutomaticCallbacks)
            {
                throw new ArgumentException("Unable to use transactions if UseAutomaticCallbacks is set to true.");
            }
            lock (_modelLock)
            {
                try
                {
                    _model.TxSelect();
                    _transactionOpen = true;
                    return true;
                }
                catch (Exception e)
                {
                    LastErrorMessage = e.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// Commit a multi publish transaction.
        /// </summary>
        /// <returns>True if successful, False otherwise.</returns>
        public bool PublishCommit()
        {
            if (UseAutomaticCallbacks)
            {
                throw new ArgumentException("Unable to use transactions if UseAutomaticCallbacks is set to true.");
            }
            lock (_modelLock)
            {
                try
                {
                    _model.TxCommit();
                    _transactionOpen = false;
                    return true;
                }
                catch (Exception e)
                {
                    LastErrorMessage = e.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// Rollback a multi publish transaction.
        /// </summary>
        /// <returns>True if successful, False otherwise.</returns>
        public bool PublishRollback()
        {
            if (UseAutomaticCallbacks)
            {
                throw new ArgumentException("Unable to use transactions if UseAutomaticCallbacks is set to true.");
            }
            lock (_modelLock)
            {
                try
                {
                    _model.TxRollback();
                    _transactionOpen = false;
                    return true;
                }
                catch (Exception e)
                {
                    LastErrorMessage = e.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// Return command response to server.
        /// </summary>
        /// <param name="command">Command object to respond to.</param>
        /// <param name="wasAccepted">True if command was accepted, False otherwise.</param>
        /// <param name="timestamp">Timestamp in UTC when command was accepted. null for current time (optional).</param>
        /// <param name="resultCode">Integer describing result of command execution (optional).</param>
        /// <param name="comment">Text field of up to 1024 characters for additional free text information (optional).</param>
        /// <returns>True if successful, False otherwise.</returns>
        public bool PublishCommandResponse(Command command, Boolean wasAccepted = true, DateTime? timestamp = null, int? resultCode = null, string comment = null)
        {
            if (UseAtomicTransactions && _transactionOpen)
            {
                throw new ArgumentException("Unable to use atomic transactions when object has an open transaction.");
            }
            if (IsConnected())
            {
                if (!timestamp.HasValue)
                {
                    timestamp = DateTime.UtcNow;
                }

                CommandResponse response = new CommandResponse()
                {
                    Id = command.Id,
                    Timestamp = command.Timestamp,
                    DeliveredAt = timestamp,
                    WasAccepted = wasAccepted,
                    ResultCode = resultCode,
                    Comment = comment
                };
                IBasicProperties properties = GetMessageProperties(2);
                string routingKey = MessageRoutingKey.GenerateDeviceCommandResponseRoutingKey(_MID, command.Id, command.Timestamp);
                string json = JsonConvert.SerializeObject(response);
                byte[] body = Encoding.UTF8.GetBytes(json);
                try
                {
                    lock (_modelLock)
                    {
                        _model.BasicPublish(_rmqExchangeName, routingKey, true, properties, body);
                    }
                    return true;
                }
                catch (Exception e)
                {
                    LastErrorMessage = e.Message;
                }
            }
            return false;
        }

        /// <summary>
        /// Sends a device pulse to the server.
        /// </summary>
        /// <param name="timestamp">Timestamp in UTC indicating the time of the pulse. null for current time.</param>
        /// <param name="expiryMilliseconds">Expiration time in millisecond for pulse message.</param>
        /// <returns>True if successful, False otherwise.</returns>
        public bool SendPulse(DateTime? timestamp, int expiryMilliseconds = 300000)
        {
            if (UseAtomicTransactions && _transactionOpen)
            {
                throw new ArgumentException("Unable to use atomic transactions when object has an open transaction.");
            }
            if (IsConnected())
            {
                if (!timestamp.HasValue)
                {
                    timestamp = DateTime.UtcNow;
                }

                Pulse pulse = new Pulse()
                {
                    Timestamp = timestamp.Value,
                    MID = _MID,
                    PulseId = 0  // Devices must always use PulseId = 0
                };

                IBasicProperties properties = GetMessageProperties(1);
                if (expiryMilliseconds > 0)
                {
                    properties.Expiration = expiryMilliseconds.ToString("F0");
                }
                string routingKey = MessageRoutingKey.GeneratePulseRoutingKey(pulse.MID);
                string json = JsonConvert.SerializeObject(pulse);
                byte[] body = Encoding.UTF8.GetBytes(json);
                try
                {
                    lock (_modelLock)
                    {
                        _model.BasicPublish(_rmqExchangeName, routingKey, true, properties, body);
                    }
                    return true;
                }
                catch (Exception e)
                {
                    LastErrorMessage = e.Message;
                }
            }
            return false;
        }

        /// <summary>
        /// Registers a new callback method that is called when a specified command is received.
        /// </summary>
        /// <param name="commandId">Command identifier.</param>
        /// <param name="commandHandler">Callback method with argument Command (e.g. "void MyCallback(string MID, Command cmd) { ... }"</param>
        public void RegisterCommandHandler(int commandId, Action<string, Command> commandHandler)
        {
            CommandSubscription<Command> commandSubscription = new CommandSubscription<Command>(_MID, commandId, commandHandler);
            _commandSubscriptions.Add(commandSubscription);
        }

        /// <summary>
        /// Removes all callback methods for a specified command.
        /// </summary>
        /// <param name="commandId">Command identifier.</param>
        public void UnregisterCommandHandler(int commandId)
        {
            foreach (CommandSubscription<Command> cs in _commandSubscriptions)
            {
                if (cs.CommandId == commandId)
                {
                    _commandSubscriptions.Remove(cs);
                }
            }
        }

        /// <summary>
        /// Registers a new callback method that is called when pulse from a specified pulse identifier is received.
        /// </summary>
        /// <param name="pulseId">Pulse identifier.</param>
        /// <param name="pulseHandler">Callback method with argument Pulse (e.g. "void MyCallback(string MID, int pulseId, Pulse pulse) { ... }"</param>
        public void RegisterPulseHandler(int pulseId, Action<string, int, Pulse> pulseHandler)
        {
            PulseSubscription pulseSubscription = new PulseSubscription(_MID, pulseId, pulseHandler);
            _pulseSubscriptions.Add(pulseSubscription);
        }

        /// <summary>
        /// Removes all callback methods from a specified pulse identifier.
        /// </summary>
        /// <param name="pulseId">Pulse identifier.</param>
        public void UnregisterPulseHandler(int pulseId)
        {
            foreach (PulseSubscription hb in _pulseSubscriptions)
            {
                if (hb.PulseId == pulseId)
                {
                    _pulseSubscriptions.Remove(hb);
                }
            }
        }

        /// <summary>
        /// Fetch next 1 incoming message in queue and dispatch if event handler is associated with it.
        /// </summary>
        public void Fetch()
        {
            lock (_modelLock)
            {
                try
                {
                    while (true)
                    {
                        BasicGetResult message = _model.BasicGet(_rmqQueueName, false);
                        if (message == null)
                        {
                            return;
                        }
                        else
                        {
                            Dispatch(message.RoutingKey, GetMessageHeader(message), message.Body.Span.ToArray(), message.DeliveryTag);
                        }
                    }
                }
                catch (Exception e)
                {
                    LastErrorMessage = e.Message;
                }
            }
        }
        #endregion

        #region InternalMethods
        private IBasicProperties GetMessageProperties(byte deliveryMode)
        {
            lock (_modelLock)
            {
                IBasicProperties properties = _model.CreateBasicProperties();
                properties.ContentType = "application/json";
                properties.DeliveryMode = deliveryMode;
                return properties;
            }
        }

        private bool PublishObservationMessage(int observationId, byte[] bytes)
        {
            if (UseAtomicTransactions && _transactionOpen)
            {
                throw new ArgumentException("Unable to use atomic transactions when object has an open transaction.");
            }
            if (IsConnected())
            {
                string routingKey = MessageRoutingKey.GenerateDeviceObservationRoutingKey(_MID, observationId);
                IBasicProperties properties = GetMessageProperties(1);
                try
                {
                    lock (_modelLock)
                    {
                        _model.BasicPublish(_rmqExchangeName, routingKey, true, properties, bytes);
                    }
                    return true;
                }
                catch (Exception e)
                {
                    LastErrorMessage = e.Message;
                }
            }
            return false;
        }

        private static IDictionary<string, object> GetMessageHeader(BasicDeliverEventArgs args)
        {
            if (args == null) return new Dictionary<string, object>();
            if (args.BasicProperties == null) return new Dictionary<string, object>();
            return args.BasicProperties.Headers;
        }

        private static IDictionary<string, object> GetMessageHeader(BasicGetResult result)
        {
            if (result == null) return new Dictionary<string, object>();
            if (result.BasicProperties == null) return new Dictionary<string, object>();
            return result.BasicProperties.Headers;
        }

        private bool Open(DeviceConnection connectionDetails)
        {
            if (_useHttps)
            {
                var ssl = new SslOption();
                ssl.Enabled = true;
                if (IgnoreSslCertificateErrors)
                {
                    ssl.AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors;
                }
                ssl.ServerName = connectionDetails.Node.MQHost;
                _connectionFactory = new ConnectionFactory
                {
                    HostName = connectionDetails.Node.MQHost,
                    Port = connectionDetails.Node.MQPortEnc,
                    UserName = _MID,
                    Password = _preSharedKey,
                    RequestedHeartbeat = new TimeSpan(0, 0, _heartbeatInterval),
                    Ssl = ssl
                };
            }
            else
            {
                _connectionFactory = new ConnectionFactory
                {
                    HostName = connectionDetails.Node.MQHost,
                    Port = connectionDetails.Node.MQPortUEnc,
                    UserName = _MID,
                    Password = _preSharedKey,
                    RequestedHeartbeat = new TimeSpan(0, 0, _heartbeatInterval)
                };
            }

            try
            {
                _connection = _connectionFactory.CreateConnection();

                if (_connection != null && _connection.IsOpen)
                {
                    lock (_modelLock)
                    {
                        _model = _connection.CreateModel();
                        _model.BasicQos(0, (ushort)this.PrefetchCount, false);
                        _transactionOpen = false;
                        return _model != null && _model.IsOpen;
                    }
                }
            }
            catch (Exception e)
            {
                LastErrorMessage = e.Message;
            }
            return false;  // Failed to create connection
        }

        private void ConsumerReceived(object sender, BasicDeliverEventArgs args)
        {
            Dispatch(args.RoutingKey, GetMessageHeader(args), args.Body.Span.ToArray(), args.DeliveryTag);
        }

        bool Dispatch(string routingKey, IDictionary<string, object> headers, byte[] body, ulong deliveryTag)
        {
            if (routingKey != null && routingKey.Length > 0)
            {
                string MID = MessageRoutingKey.ParseMID(routingKey);
                if (MID == _MID && body != null && body.Length > 0)
                {
                    if (MessageRoutingKey.IsDeviceCommand(routingKey))
                    {
                        string json = Encoding.UTF8.GetString(body);
                        Command command = JsonConvert.DeserializeObject<Command>(json);
                        CommandSubscription<Command> cs = _commandSubscriptions.Find(s => s.CommandId == command.Id);
                        if (cs != null)
                        {
                            cs.CommandHandler(MID, command);
                            lock (_modelLock)
                            {
                                _model.BasicAck(deliveryTag, false);
                            }
                            return true;
                        }
                    }
                    else if (MessageRoutingKey.IsApplicationPulse(routingKey))
                    {
                        string json = Encoding.UTF8.GetString(body);
                        Pulse pulse = JsonConvert.DeserializeObject<Pulse>(json);
                        PulseSubscription ps = _pulseSubscriptions.Find(s => s.PulseId == pulse.PulseId);
                        if (ps != null)
                        {
                            ps.PulseHandler(MID, pulse.PulseId, pulse);
                            lock (_modelLock)
                            {
                                _model.BasicAck(deliveryTag, false);
                            }
                            return true;
                        }
                    }
                }
                lock (_modelLock)
                {
                    _model.BasicNack(deliveryTag, false, false);
                }
            }
            return false;
        }
        #endregion  // InternalMethods
    }
}
