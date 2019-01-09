﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.MixedReality.Toolkit.Core.Definitions.Utilities;
using Microsoft.MixedReality.Toolkit.Core.EventDatum.Input;
using Microsoft.MixedReality.Toolkit.Core.Interfaces.DataProviders.Controllers;
using Microsoft.MixedReality.Toolkit.Core.Interfaces.InputSystem.Handlers;
using Microsoft.MixedReality.Toolkit.Core.Services;
using Microsoft.MixedReality.Toolkit.SDK.Utilities.Solvers;
using UnityEngine;

namespace Microsoft.MixedReality.Toolkit.Examples.Demos
{
    /// <summary>
    /// This class is used in the SolverExamples scene, used to swap between active solvers
    /// and placeholder solvers displayed in the scene.
    /// </summary>
    public class SwapVolume : MonoBehaviour, IMixedRealityPointerHandler
    {
        [SerializeField]
        [Tooltip("The scene object to be hidden when the active solver is enabled.")]
        private GameObject hideThisObject = null;

        [SerializeField]
        [Tooltip("The solver prefab to be spawned and used when this volume is activated.")]
        private GameObject spawnThisPrefab = null;

        [SerializeField]
        [Tooltip("Whether to update the solver's target to be the controller that clicked on the volume or not.")]
        private bool updateSolverTargetToClickSource = true;

        private SolverHandler solverHandler;
        private GameObject spawnedObject;

        private void Awake()
        {
            // This example script depends on both GameObjects being properly set.
            if (hideThisObject == null || spawnThisPrefab == null)
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            spawnedObject = Instantiate(spawnThisPrefab, hideThisObject.transform.position, hideThisObject.transform.rotation);
            spawnedObject.SetActive(false);
            solverHandler = spawnedObject.GetComponent<SolverHandler>();
        }


        public void OnPointerUp(MixedRealityPointerEventData eventData) { }

        public void OnPointerDown(MixedRealityPointerEventData eventData) { }

        public void OnPointerClicked(MixedRealityPointerEventData eventData)
        {
            if (spawnedObject.activeSelf)
            {
                spawnedObject.SetActive(false);
                hideThisObject.SetActive(true);
            }
            else
            {
                spawnedObject.SetActive(true);

                if (updateSolverTargetToClickSource && solverHandler != null)
                {
                    if (MixedRealityToolkit.InputSystem.TryGetController(eventData.InputSource, out IMixedRealityController controller))
                    {
                        if (controller.ControllerHandedness == Handedness.Right)
                        {
                            solverHandler.TrackedObjectToReference = TrackedObjectType.MotionControllerRight;
                        }
                        else if (controller.ControllerHandedness == Handedness.Left)
                        {
                            solverHandler.TrackedObjectToReference = TrackedObjectType.MotionControllerLeft;
                        }
                    }
                }

                hideThisObject.SetActive(false);
            }

            eventData.Use(); // Mark the event as used, so it doesn't fall through to other handlers.
        }

        private void OnDestroy()
        {
            Destroy(spawnedObject);
            Destroy(hideThisObject);
        }
    }
}
