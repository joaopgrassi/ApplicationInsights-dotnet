﻿namespace Microsoft.ApplicationInsights.Extensibility.Implementation.External
{
    using System.Diagnostics;
    using System.Linq;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;

    /// <summary>
    /// Partial class to implement IExtension
    /// </summary>
    internal partial class MetricData : IExtension
    {
        public void Serialize(ISerializationWriter serializationWriter)
        {
            serializationWriter.WriteProperty("ver", this.ver);            
            serializationWriter.WriteProperty("metrics", this.metrics.ToList<IExtension>());
            serializationWriter.WriteProperty("properties", this.properties);
        }

        IExtension IExtension.DeepClone()
        {
            return this.DeepClone();
        }
    }
}