﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.﻿

using UnityEngine;

namespace Microsoft.MixedReality.Toolkit.Core.Definitions
{
    /// <summary>
    /// The base profile to use for custom <see cref="Interfaces.IMixedRealityExtensionService"/>s
    /// </summary>
    public abstract class BaseMixedRealityExtensionServiceProfile : BaseMixedRealityProfile
    {
        [SerializeField]
        [Tooltip("Currently registered IMixedRealityDataProvider configurations for this extension service.")]
        private DataModelConfiguration[] registeredDataProviders = null;

        /// <summary>
        /// Currently registered <see cref="Interfaces.IMixedRealityDataProvider"/> configurations for this extension service.
        /// </summary>
        public DataModelConfiguration[] RegisteredDataProviders
        {
            get { return registeredDataProviders; }
            set { registeredDataProviders = value; }
        }
    }
}