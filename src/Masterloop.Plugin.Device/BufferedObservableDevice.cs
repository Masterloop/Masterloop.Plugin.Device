using Masterloop.Core.Types.Base;
using Masterloop.Core.Types.Observations;
using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;

namespace Masterloop.Plugin.Device
{
    public class BufferedObservation
    {
        public int Id { get; set; }
        public int ObservationId { get; set; }
        public int DataType { get; set; }
        public DateTime Timestamp { get; set; }
        public string Value { get; set; }
        public bool Uploaded { get; set; }
    }

    public class BufferedObservableDevice : ObservableDevice
    {
        #region PrivateMembers
        const string _OBSBUF_NAME = "Observations";
        string _liteDBPathName;
        #endregion

        #region Properties
        public int UploadRowLimit { get; set; }
        public bool UploadSuccess { get; set; }
        #endregion

        #region Construction
        /// <summary>
        /// Constructs a new BufferedObservableDevice object.
        /// </summary>
        /// <param name="MID">Device identifier.</param>
        /// <param name="preSharedKey">Pre shared key for device.</param>
        /// <param name="hostName">Host to connect to, e.g. "myserver.example.com" or "10.0.0.2".</param>
        /// <param name="bufferPathName">Path and filename of buffer file. This process must be able to read/write to this file.</param>
        /// <param name="useHttps">True if using HTTPS (SSL/TLS), False if using HTTP (unencrypted).</param>
        public BufferedObservableDevice(string MID, String preSharedKey, String hostName, string bufferPathName, bool useHttps = true)
            : base(MID, preSharedKey, hostName, useHttps)
        {
            this._liteDBPathName = bufferPathName;
            this.UploadRowLimit = 1000;
            this.UploadSuccess = false;
            base.NotifyListenersOnUpload = false;
        }
        #endregion

        #region Operations
        /// <summary>
        /// Stores observations in observation package in buffer and tries to upload buffer to server.
        /// </summary>
        /// <param name="package">>Observation data of type ObservationPackage.</param>
        /// <param name="uploadBuffer">True to upload buffer, False to bypass uploading.</param>
        /// <returns>True if uploading or buffering of observations was successful, false otherwise.</returns>
        public bool LogObservations(ObservationPackage package, bool uploadBuffer = true)
        {
            // Online, upload buffer and log package.
            if (uploadBuffer)
            {
                UploadSuccess = false;
                if (UploadBuffer())
                {
                    if (base.LogObservations(package))
                    {
                        UploadSuccess = true;
                        return true;
                    }
                }
            }

            // Offline, store new package to buffer.
            foreach (IdentifiedObservations io in package.Observations)
            {
                if (!Store(io.ObservationId, io.Observations))
                {
                    return false;  // Storing to buffer failed.
                }
            }
            return true;
        }

        /// <summary>
        /// Stores observation in buffer and tries to upload buffer to server.
        /// </summary>
        /// <param name="observationId">Observation identifier.</param>
        /// <param name="observation">Observation.</param>
        /// <param name="uploadBuffer">True to upload buffer, False to bypass uploading.</param>
        /// <returns>True if uploading or buffering of observation was successful, false otherwise.</returns>
        public bool LogObservation(int observationId, Observation observation, bool uploadBuffer = true)
        {
            // Online, upload buffer and log package.
            if (uploadBuffer)
            {
                UploadSuccess = false;
                if (UploadBuffer())
                {
                    ObservationPackageBuilder bldr = new ObservationPackageBuilder();
                    bldr.Append(observationId, observation);
                    if (base.LogObservations(bldr.GetAsObservationPackage()))
                    {
                        UploadSuccess = true;
                        return true;
                    }
                }
            }

            // Offline, store new package to buffer.
            Observation[] observations = new Observation[] { observation };
            return Store(observationId, observations);
        }

        /// <summary>
        /// Stores observations in buffer and tries to upload buffer to server.
        /// </summary>
        /// <param name="observationId">Observation identifier.</param>
        /// <param name="observations">Array of observations.</param>
        /// <param name="uploadBuffer">True to upload buffer, False to bypass uploading.</param>
        /// <returns>True if uploading or buffering of observations was successful, false otherwise.</returns>
        public bool LogObservations(int observationId, Observation[] observations, bool uploadBuffer = true)
        {
            // Online, upload buffer and log package.
            if (uploadBuffer)
            {
                UploadSuccess = false;
                if (UploadBuffer())
                {
                    ObservationPackageBuilder bldr = new ObservationPackageBuilder();
                    IdentifiedObservations ios = new IdentifiedObservations()
                    {
                        ObservationId = observationId,
                        Observations = observations
                    };
                    bldr.Append(ios);
                    if (base.LogObservations(bldr.GetAsObservationPackage()))
                    {
                        UploadSuccess = true;
                        return true;
                    }
                }
            }

            // Offline, store new package to buffer.
            return Store(observationId, observations);
        }

        /// <summary>
        /// Upload buffer to server if connection is available.
        /// </summary>
        /// <returns>True if uploading was successful, and false otherwise.</returns>
        public bool UploadBuffer()
        {
            // If network is available and buffer contains data.
            if (GetBufferedRowCount() > 0)
            {
                if (CanPingServer())
                {
                    bool loggingSuccessful = false;
                    do
                    {
                        int maxRowId = -1;
                        ObservationPackage package = GetBufferedObservations(out maxRowId);
                        if (maxRowId != -1)
                        {
                            loggingSuccessful = base.LogObservations(package);
                            if (loggingSuccessful)
                            {
                                ClearBuffer(maxRowId);
                            }
                        }
                    }
                    while (loggingSuccessful && GetBufferedRowCount() > 0);
                    return loggingSuccessful;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Marks all observations in buffer as uploaded.
        /// </summary>
        public void ClearBuffer()
        {
            using (LiteDatabase db = new LiteDatabase(_liteDBPathName))
            {
                var col = db.GetCollection<BufferedObservation>(_OBSBUF_NAME);
                var records = col.FindAll();
                foreach (var r in records)
                {
                    r.Uploaded = true;
                    col.Update(r);
                }
            }
        }

        /// <summary>
        /// Mark all observations in buffer as uploaded, up to specified row id.
        /// </summary>
        /// <param name="toId">Up to observation row identifier in buffer.</param>
        public void ClearBuffer(int toId)
        {
            using (LiteDatabase db = new LiteDatabase(_liteDBPathName))
            {
                var col = db.GetCollection<BufferedObservation>(_OBSBUF_NAME);
                var records = col.Find(c => c.Id <= toId);
                foreach (var r in records)
                {
                    r.Uploaded = true;
                    col.Update(r);
                }
            }
        }

        /// <summary>
        /// Mark all observations in buffer as uploaded, between two timestamps.
        /// </summary>
        /// <param name="fromTimestamp">Start timestamp to remove.</param>
        /// <param name="toTimestamp">End timestamp to remove.</param>
        public void ClearBuffer(DateTime fromTimestamp, DateTime toTimestamp)
        {
            using (LiteDatabase db = new LiteDatabase(_liteDBPathName))
            {
                var col = db.GetCollection<BufferedObservation>(_OBSBUF_NAME);
                var records = col.Find(c => c.Timestamp >= fromTimestamp && c.Timestamp <= toTimestamp);
                foreach (var r in records)
                {
                    r.Uploaded = true;
                    col.Update(r);
                }
            }
        }

        /// <summary>
        /// Physically deletes all buffered observations in buffer.
        /// </summary>
        public void EmptyBuffer()
        {
            using (LiteDatabase db = new LiteDatabase(_liteDBPathName))
            {
                db.DropCollection(_OBSBUF_NAME);
            }
        }

        /// <summary>
        /// Gets the total number of non-uploaded observations in buffer.
        /// </summary>
        /// <returns>Number of observations in buffer.</returns>
        public int GetBufferedRowCount()
        {
            using (LiteDatabase db = new LiteDatabase(_liteDBPathName))
            {
                var col = db.GetCollection<BufferedObservation>(_OBSBUF_NAME);
                return col.Find(c => c.Uploaded == false).Count();
            }
        }
        #endregion

        #region Private Methods
        private bool Store(int observationId, Observation[] observations)
        {
            using (LiteDatabase db = new LiteDatabase(_liteDBPathName))
            {
                var col = db.GetCollection<BufferedObservation>(_OBSBUF_NAME);

                foreach (Observation o in observations)
                {
                    BufferedObservation bo = new BufferedObservation()
                    {
                        ObservationId = observationId,
                        Timestamp = o.Timestamp,
                    };
                    if (o.GetType() == typeof(BooleanObservation))
                    {
                        bo.DataType = (int)DataType.Boolean;
                        bo.Value = DataTypeStringConverter.FormatBoolean((o as BooleanObservation).Value);
                    }
                    else if (o.GetType() == typeof(DoubleObservation))
                    {
                        bo.DataType = (int)DataType.Double;
                        bo.Value = DataTypeStringConverter.FormatDouble((o as DoubleObservation).Value);
                    }
                    else if (o.GetType() == typeof(IntegerObservation))
                    {
                        bo.DataType = (int)DataType.Integer;
                        bo.Value = DataTypeStringConverter.FormatInteger((o as IntegerObservation).Value);
                    }
                    else if (o.GetType() == typeof(PositionObservation))
                    {
                        bo.DataType = (int)DataType.Position;
                        bo.Value = DataTypeStringConverter.FormatPosition((o as PositionObservation).Value);
                    }
                    else if (o.GetType() == typeof(StringObservation))
                    {
                        bo.DataType = (int)DataType.String;
                        bo.Value = (o as StringObservation).Value;
                    }
                    else if (o.GetType() == typeof(StatisticsObservation))
                    {
                        bo.DataType = (int)DataType.Statistics;
                        bo.Value = DataTypeStringConverter.FormatStatistics((o as StatisticsObservation).Value);
                    }
                    else
                    {
                        throw new ArgumentException("Unknown observation data type: " + o.GetType().ToString());
                    }

                    col.Insert(bo);
                }
                return true;
            }
        }

        private ObservationPackage GetBufferedObservations(out int maxId)
        {
            maxId = -1;
            ObservationPackageBuilder builder = new ObservationPackageBuilder();
            using (LiteDatabase db = new LiteDatabase(_liteDBPathName))
            {
                var col = db.GetCollection<BufferedObservation>(_OBSBUF_NAME);

                var records = col.Find(c => c.Uploaded == false).OrderBy(c => c.Id);

                IEnumerable<BufferedObservation> observations = null;
                if (UploadRowLimit > 0)
                {
                    observations = records.Take(UploadRowLimit);
                }
                else
                {
                    observations = records;
                }

                foreach (BufferedObservation bo in observations)
                {
                    maxId = bo.Id;
                    if (bo.Value != null)
                    {
                        Observation observation = null;
                        switch ((DataType)(bo.DataType))
                        {
                            case DataType.Boolean: observation = new BooleanObservation() { Timestamp = bo.Timestamp, Value = DataTypeStringConverter.ParseBooleanValue(bo.Value) }; break;
                            case DataType.Double: observation = new DoubleObservation() { Timestamp = bo.Timestamp, Value = DataTypeStringConverter.ParseDoubleValue(bo.Value) }; break;
                            case DataType.Integer: observation = new IntegerObservation() { Timestamp = bo.Timestamp, Value = DataTypeStringConverter.ParseIntegerValue(bo.Value) }; break;
                            case DataType.Position: observation = new PositionObservation() { Timestamp = bo.Timestamp, Value = DataTypeStringConverter.ParsePositionValue(bo.Value) }; break;
                            case DataType.String: observation = new StringObservation() { Timestamp = bo.Timestamp, Value = bo.Value }; break;
                            case DataType.Statistics: observation = new StatisticsObservation() { Timestamp = bo.Timestamp, Value = DataTypeStringConverter.ParseStatisticsValue(bo.Value) }; break;
                            default: throw new NotSupportedException("Unknown data type: " + bo.DataType.ToString());
                        }
                        IdentifiedObservation io = new IdentifiedObservation()
                        {
                            ObservationId = bo.ObservationId,
                            Observation = observation
                        };
                        builder.Append(io);
                    }
                }
            }
            return builder.GetAsObservationPackage();
        }
        #endregion
    }
}
