﻿using System.Text.Json.Serialization;
using DuetAPI.Utility;

namespace DuetAPI.ObjectModel
{
    /// <summary>
    /// Possible state of a spindle
    /// </summary>
    [JsonConverter(typeof(JsonLowerCaseStringEnumConverter))]
    public enum SpindleState
    {
        /// <summary>
        /// Spinde not configured
        /// </summary>
        Unconfigured,

        /// <summary>
        /// Spindle is stopped (inactive)
        /// </summary>
        Stopped,

        /// <summary>
        /// Spindle is going forwards
        /// </summary>
        Forward,

        /// <summary>
        /// Spindle is going in reverse
        /// </summary>
        Reverse
    }
}
