﻿// AdaptiveDockingNode
//
// ModuleAdaptiveDockingNode.cs
//
// Copyright © 2014, toadicus
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the following conditions are met:
//
// 1. Redistributions of source code must retain the above copyright notice,
//    this list of conditions and the following disclaimer.
//
// 2. Redistributions in binary form must reproduce the above copyright notice,
//    this list of conditions and the following disclaimer in the documentation and/or other
//    materials provided with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
// INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
// WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using KSP;
using System;
using System.Collections.Generic;
using System.Linq;
using ToadicusTools;
using UnityEngine;

namespace AdaptiveDockingNode
{
	public class ModuleAdaptiveDockingNode : PartModule
	{
		[KSPField(isPersistant = false)]
		public string ValidSizes;

		protected ModuleDockingNode dockingModule;
		protected AttachNode referenceAttachNode;

		protected double vesselFilterDistanceSqr;

		protected bool hasAttachedState;

		protected System.Diagnostics.Stopwatch timeoutTimer;

		protected float acquireRangeSqr
		{
			get
			{
				if (this.dockingModule == null)
				{
					return float.NaN;
				}

				return this.dockingModule.acquireRange * this.dockingModule.acquireRange;
			}
		}

		protected Part attachedPart
		{
			get
			{
				if (this.referenceAttachNode == null)
				{
					return null;
				}

				return this.referenceAttachNode.attachedPart;
			}
		}

		public string currentSize
		{
			get
			{
				if (this.dockingModule == null)
				{
					return this.defaultSize;
				}

				return this.dockingModule.nodeType;
			}
			set
			{
				if (this.dockingModule == null)
				{
					return;
				}

				this.dockingModule.nodeType = value;
			}
		}

		public string defaultSize
		{
			get;
			protected set;
		}

		protected bool hasAttachedPart
		{
			get
			{
				return (this.attachedPart == null);
			}
		}

		public List<string> validSizes
		{
			get;
			protected set;
		}

		public override void OnStart(StartState state)
		{
			base.OnStart(state);

			/*switch (state)
			{
				case StartState.Editor:
				case StartState.None:
					Tools.PostDebugMessage(this, "Refusing to start when not in flight.");
					return;
				default:
					break;
			}*/

			if (this.ValidSizes != string.Empty)
			{
				this.validSizes = this.ValidSizes.Split(',').Select(s => s.Trim()).ToList();
				this.validSizes.Sort();
				this.validSizes.Reverse();

				this.defaultSize = this.validSizes[0];

				Tools.PostDebugMessage(this, "Loaded!" +
					"\n\tvalidSizes: {0}" +
					"\n\tdefaultSize: {1}",
					string.Join(", ", this.validSizes.Select(s => (string)s).ToArray()),
					this.defaultSize
				);
			}

			if (this.validSizes == null || this.validSizes.Count == 0)
			{
				Tools.PostDebugMessage(this,
					"Refusing to start because our module was configured poorly." +
					"\n\tvalidSizes: {0}",
					this.validSizes
				);
				return;
			}

			this.timeoutTimer = new System.Diagnostics.Stopwatch();

			this.dockingModule = this.part.getFirstModuleOfType<ModuleDockingNode>();

			if (this.dockingModule == null)
			{
				Tools.PostDebugMessage(this, "Failed startup because a docking module could not be found.");
				return;
			}

			this.dockingModule.Fields["nodeType"].isPersistant = true;

			#if DEBUG
			this.dockingModule.Fields["nodeType"].guiActive = true;
			this.dockingModule.Fields["nodeType"].guiName = "Node Size";
			#endif

			if (this.dockingModule.referenceAttachNode != string.Empty)
			{
				Tools.PostDebugMessage(this,
					string.Format("referenceAttachNode string: {0}", this.dockingModule.referenceAttachNode));

				this.referenceAttachNode = this.part.attachNodes
					.FirstOrDefault(n => n.id == this.dockingModule.referenceAttachNode);

				Tools.PostDebugMessage(this,
					string.Format("referenceAttachNode: {0}", this.referenceAttachNode));
			}

			var config = KSP.IO.PluginConfiguration.CreateForType<ModuleAdaptiveDockingNode>();
			config.load();

			this.vesselFilterDistanceSqr = config.GetValue("vesselFilterDistance", 1000d);
			config.SetValue("vesselFilterDistance", this.vesselFilterDistanceSqr);

			this.vesselFilterDistanceSqr *= this.vesselFilterDistanceSqr;

			config.save();

			this.hasAttachedState = !this.hasAttachedPart;

			Tools.PostDebugMessage(this, "Started!",
				string.Format("dockingModule: {0}", this.dockingModule)
			);
		}

		public void FixedUpdate()
		{
			if (HighLogic.LoadedSceneIsFlight && FlightGlobals.Vessels != null && this.dockingModule != null)
			{
				bool foundApproach = false;

				Tools.DebugLogger verboseLog = Tools.DebugLogger.New(this);

				verboseLog.AppendFormat(" ({0}_{1}) on {2}",
					this.part.partInfo.name, this.part.uid, this.vessel.vesselName);
				verboseLog.AppendFormat("\nChecking within acquireRangeSqr: {0}", this.acquireRangeSqr);

				if (this.timeoutTimer.IsRunning && this.timeoutTimer.ElapsedMilliseconds > 5000)
				{
					verboseLog.AppendFormat("\nNo target detected within 5 seconds, timing out.");
					verboseLog.AppendFormat("\nReverting to default nodeType: {0}", this.defaultSize);

					this.dockingModule.nodeType = this.defaultSize;
					this.timeoutTimer.Reset();
				}

				bool foundTargetNode = false;
				float closestNodeDistSqr = this.acquireRangeSqr * 4f;

				foreach (Vessel vessel in FlightGlobals.Vessels)
				{
					#if DEBUG
					if (vessel == this.vessel)
						continue;
					#endif

					// Skip vessels that are just way too far away.
					if (this.vessel.sqrDistanceTo(vessel) > this.vesselFilterDistanceSqr)
					{
						verboseLog.AppendFormat("\nSkipping distant vessel {0} (sqrDistance {1})",
							vessel.vesselName, (vessel.GetWorldPos3D() - this.dockingModule.transform.position).sqrMagnitude);
						continue;
					}

					verboseLog.AppendFormat("\nChecking nearby vessel {0} (sqrDistance {1})",
						vessel.vesselName, (vessel.GetWorldPos3D() - this.dockingModule.transform.position).sqrMagnitude);

					foreach (ModuleDockingNode potentialTargetNode in vessel.getModulesOfType<ModuleDockingNode>())
					{
						verboseLog.AppendFormat("\nFound potentialTargetNode: {0}", potentialTargetNode);
						verboseLog.AppendFormat("\n\tpotentialTargetNode.state: {0}", potentialTargetNode.state);
						verboseLog.AppendFormat("\n\tpotentialTargetNode.nodeType: {0}", potentialTargetNode.nodeType);

						if (potentialTargetNode.part == this.part)
						{
							verboseLog.AppendFormat("\nDiscarding potentialTargetNode: on this part.");
							continue;
						}

						if (
							potentialTargetNode.state.Contains(string.Intern("Docked")) ||
							potentialTargetNode.state.Contains(string.Intern("PreAttached")))
						{
							verboseLog.Append("\nDiscarding potentialTargetNode: not ready.");
							continue;
						}

						float thisNodeDistSqr = (potentialTargetNode.nodeTransform.position - this.dockingModule.nodeTransform.position).sqrMagnitude;

						verboseLog.AppendFormat(
							"\n\tChecking potentialTargetNode sqrDistance against the lesser of acquireRangeSqr and " +
							"closestNodeDistSqr ({0})",
							Mathf.Min(this.acquireRangeSqr * 4f, closestNodeDistSqr)
						);

						if (thisNodeDistSqr <= Mathf.Min(this.acquireRangeSqr * 4f, closestNodeDistSqr))
						{
							foundApproach = true;

							verboseLog.AppendFormat(
								"\n\tpotentialTargetNode is nearby ({0}), checking if adaptive.", thisNodeDistSqr);

							ModuleAdaptiveDockingNode targetAdaptiveNode = null;

							string targetSize;
							targetSize = string.Empty;

							if (this.validSizes.Contains(potentialTargetNode.nodeType))
							{
								targetSize = potentialTargetNode.nodeType;
								this.currentSize = targetSize;
							}
							else
							{
								// Check the part for an AdaptiveDockingNode
								targetAdaptiveNode = potentialTargetNode.part.getFirstModuleOfType<ModuleAdaptiveDockingNode>();
							}

							if (targetAdaptiveNode == null)
							{
								verboseLog.AppendFormat("\n\tpotentialTargetNode is not adaptive.");
								verboseLog.AppendFormat("\n\tnodeType: {0}", potentialTargetNode.nodeType);
							}
							else
							{
								verboseLog.AppendFormat("\n\tpotentialTargetNode is adaptive.");
								verboseLog.AppendFormat("\n\tdefaultSize: {0}", targetAdaptiveNode.defaultSize);
								verboseLog.AppendFormat("\n\tvalidSizes: {0}", targetAdaptiveNode.validSizes);

								if (this.validSizes.Contains(targetAdaptiveNode.defaultSize))
								{
									targetSize = targetAdaptiveNode.defaultSize;
								}
								else
								{
									string commonNodeType = GetGreatestCommonNodeType(this, targetAdaptiveNode);

									if (commonNodeType == string.Empty)
									{
										verboseLog.AppendFormat("\n\tInvalid adaptive target: no common node types.");
										continue;
									}

									targetAdaptiveNode.currentSize = commonNodeType;
									this.currentSize = commonNodeType;

									verboseLog.AppendFormat("\n\tLocal and target nodeTypes set to commonNodeType: {0}");
								}
							}

							if (targetSize == string.Empty)
							{
								continue;
							}

							verboseLog.AppendFormat("\n\tFound suitable docking node.");
							verboseLog.AppendFormat("\n\ttargetSize: {0}", targetSize);
						
							verboseLog.AppendFormat("\n\tForward vector dot product: {0} (acquire minimum: {1})",
								Vector3.Dot(potentialTargetNode.transform.forward, this.dockingModule.transform.forward),
								this.dockingModule.acquireMinFwdDot
							);

							verboseLog.AppendFormat("\n\tUp vector dot product: {0} (acquire minimum: {1})",
								Vector3.Dot(potentialTargetNode.transform.up, this.dockingModule.transform.up),
								this.dockingModule.acquireMinRollDot
							);

							closestNodeDistSqr = thisNodeDistSqr;
							this.currentSize = targetSize;
							foundTargetNode = true;
						}
						else
						{
							verboseLog.AppendFormat(
								"\nDiscarding potentialTargetNode: too far away (thisNodeDistSqr: {0})",
								thisNodeDistSqr
							);
						}
					}

					verboseLog.Append('\n');
				}

				verboseLog.Append("\nFixedUpdate Finished.");

				if (foundApproach)
					verboseLog.Print();

				if (foundTargetNode)
				{
					if (this.timeoutTimer.IsRunning)
					{
						this.timeoutTimer.Reset();
					}

					this.timeoutTimer.Start();
				}
			}
		}

		public void LateUpdate()
		{
			if (HighLogic.LoadedSceneIsEditor)
			{
				Tools.PostDebugMessage(this, "LateUpdate", string.Format("attachedPart: {0}", this.attachedPart));

				if (this.hasAttachedPart != this.hasAttachedState)
				{
					this.hasAttachedState = this.hasAttachedPart;

					if (this.attachedPart != null)
					{
						ModuleDockingNode attachedNode = this.attachedPart.getFirstModuleOfType<ModuleDockingNode>();

						Tools.PostDebugMessage(this, string.Format("attachedNode: {0}", attachedNode));

						if (attachedNode != null)
						{
							ModuleAdaptiveDockingNode attachedADN = this.attachedPart
								.getFirstModuleOfType<ModuleAdaptiveDockingNode>();

							if (attachedADN != null && attachedADN.currentSize != this.currentSize)
							{
								this.currentSize = GetGreatestCommonNodeType(this, attachedADN);

								Tools.PostDebugMessage(this,
									string.Format("Attached to AdaptiveDockingNode, setting currentSize = {0}",
										this.currentSize)
								);
							}
							else if (attachedNode.nodeType != this.currentSize)
							{
								this.currentSize = attachedNode.nodeType;

								Tools.PostDebugMessage(this,
									string.Format("Attached to ModuleDockingNode, setting currentSize = {0}",
										this.currentSize)
								);
							}
						}
					}
				}
			}
		}

		public bool SetDefaultNodeType()
		{
			if (this.dockingModule == null || this.defaultSize == string.Empty)
			{
				return false;
			}

			this.currentSize = this.defaultSize;
			return true;
		}

		public static string GetGreatestCommonNodeType(ModuleAdaptiveDockingNode n1, ModuleAdaptiveDockingNode n2)
		{
			int count1 = 0;
			int count2 = 0;

			while (count1 < n1.validSizes.Count && count2 < n2.validSizes.Count)
			{
				int compareResult;

				compareResult = n1.validSizes[count1].CompareTo(n2.validSizes[count2]);

				if (compareResult == 0)
				{
					return n1.validSizes[count1];
				}
				else if (compareResult > 0)
				{
					count1++;
				}
				else
				{
					count2++;
				}
			}

			return string.Empty;
		}
	}
}
