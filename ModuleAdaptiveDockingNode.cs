// AdaptiveDockingNode
//
// ModuleAdaptiveDockingNode.cs
//
// Copyright © 2014-2015, toadicus
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
using ToadicusTools;
using ToadicusTools.Extensions;
using UnityEngine;

namespace AdaptiveDockingNode
{
	public class ModuleAdaptiveDockingNode : PartModule
	{
		[KSPField(isPersistant = false)]
		public string ValidSizes;

		[KSPField(isPersistant = false)]
		public string Gender;

		protected ModuleDockingNode dockingModule;
		protected AttachNode referenceAttachNode;

		protected PortGender portGender;

		protected double vesselFilterDistanceSqr;

		protected bool hasAttachedState;

		protected System.Diagnostics.Stopwatch timeoutTimer;

		protected string GuidString;

		protected float acquireRangeSqr
		{
			get;
			set;
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

		public override void OnAwake()
		{
			base.OnAwake();

			this.portGender = PortGender.NEUTRAL;
		}

		public override void OnStart(StartState state)
		{
			base.OnStart(state);

			/*switch (state)
			{
				case StartState.Editor:
				case StartState.None:
					Logging.PostDebugMessage(this, "Refusing to start when not in flight.");
					return;
				default:
					break;
			}*/

			if (this.ValidSizes != string.Empty)
			{
				this.validSizes = new List<string>();

				string[] splitSizes = this.ValidSizes.Split(',');

				for (int idx = 0; idx < splitSizes.Length; idx++)
				{
					this.validSizes.Add(splitSizes[idx].Trim());
				}

				this.validSizes.Sort();
				this.validSizes.Reverse();

				this.defaultSize = this.validSizes[0];
			}

			if (this.validSizes == null || this.validSizes.Count == 0)
			{
				Logging.PostDebugMessage(this,
					"Refusing to start because our module was configured poorly." +
					"\n\tvalidSizes: {0}",
					this.validSizes
				);
				return;
			}

			if (this.Gender != null)
			{
				string trimmedGender = this.Gender.Trim().ToLower();

				if (trimmedGender == "female")
				{
					this.portGender = PortGender.FEMALE;
				}
				else if (trimmedGender == "male")
				{
					this.portGender = PortGender.MALE;
				}

				if (HighLogic.LoadedSceneIsFlight)
				{
					switch (this.portGender)
					{
						case PortGender.FEMALE:
						case PortGender.MALE:
							byte[] partUID = BitConverter.GetBytes(this.part.flightID);
							byte[] vesselUID = this.vessel.id.ToByteArray();
							byte[] guidBytes = new byte[partUID.Length + vesselUID.Length];

							partUID.CopyTo(guidBytes, 0);
							vesselUID.CopyTo(guidBytes, partUID.Length);

							this.GuidString = Convert.ToBase64String(guidBytes).TrimEnd('=');

							this.defaultSize = String.Format("{0}_{1}_{2}", this.defaultSize, trimmedGender, this.GuidString);
							break;
						default:
							break;
					}
				}
			}

			Logging.PostDebugMessage(this, "Loaded!" +
				"\n\tdefaultSize: {0}",
				this.defaultSize
			);

			Logging.PostDebugMessage(this, "Port gender is {0}", Enum.GetName(typeof(PortGender), this.portGender));

			this.timeoutTimer = new System.Diagnostics.Stopwatch();

			this.dockingModule = this.part.getFirstModuleOfType<ModuleDockingNode>();

			if (this.dockingModule == null)
			{
				Logging.PostDebugMessage(this, "Failed startup because a docking module could not be found.");
				return;
			}

			this.dockingModule.Fields["nodeType"].isPersistant = true;

			// If we're not in the editor, not docked, and not preattached, set the current size to the default size.
			// This stops gendered ports from matching non-adaptive ports.
			if (
				!HighLogic.LoadedSceneIsEditor &&
				this.dockingModule.state != "Docked" &&
				this.dockingModule.state != "PreAttached"
			)
			{
				this.currentSize = this.defaultSize;
			}

			#if DEBUG
			this.dockingModule.Fields["nodeType"].guiActive = true;
			this.dockingModule.Fields["nodeType"].guiName = "Node Size";
			#endif

			if (this.dockingModule.referenceAttachNode != string.Empty)
			{
				Logging.PostDebugMessage(this,
					string.Format("referenceAttachNode string: {0}", this.dockingModule.referenceAttachNode));

				AttachNode node;
				for (int nIdx = 0; nIdx < this.part.attachNodes.Count; nIdx++)
				{
					node = this.part.attachNodes[nIdx];

					if (node.id == this.dockingModule.referenceAttachNode)
					{
						this.referenceAttachNode = node;
						break;
					}
				}

				Logging.PostDebugMessage(this,
					string.Format("referenceAttachNode: {0}", this.referenceAttachNode));
			}

			this.acquireRangeSqr = this.dockingModule.acquireRange * this.dockingModule.acquireRange;

			var config = KSP.IO.PluginConfiguration.CreateForType<ModuleAdaptiveDockingNode>();
			config.load();

			this.vesselFilterDistanceSqr = config.GetValue("vesselFilterDistance", 1000d);
			config.SetValue("vesselFilterDistance", this.vesselFilterDistanceSqr);

			this.vesselFilterDistanceSqr *= this.vesselFilterDistanceSqr;

			config.save();

			this.hasAttachedState = !this.hasAttachedPart;

			Logging.PostDebugMessage(this, "Started!",
				string.Format("dockingModule: {0}", this.dockingModule)
			);
		}

		public void FixedUpdate()
		{
			if (
				HighLogic.LoadedSceneIsFlight &&
				FlightGlobals.Vessels != null &&
				this.vessel != null &&
				this.dockingModule != null
			)
			{
				#if DEBUG
				bool foundApproach = false;
				#endif

				PooledDebugLogger verboseLog = PooledDebugLogger.New(this);

				#if DEBUG
				try
				{
				#endif

				verboseLog.AppendFormat(" ({0}_{1}) on {2}",
					this.part.partInfo.name, this.part.craftID, this.vessel.vesselName);
				verboseLog.AppendFormat("\nChecking within acquireRangeSqr: {0}", this.acquireRangeSqr);
				
				verboseLog.AppendFormat("this.dockingModule: {0}\n", this.dockingModule == null ?
						"null" : this.dockingModule.ToString());
				
				verboseLog.AppendFormat("this.dockingModule.state: {0}\n",
					this.dockingModule == null || this.dockingModule.state == null ?
					"null" :this.dockingModule.state.ToString());

				// If we're already docked or pre-attached...
				if (this.dockingModule.state == "Docked" || this.dockingModule.state == "PreAttached")
				{
					// ...and if the timeout timer is running...
					if (this.timeoutTimer.IsRunning)
					{
						// ...reset the timeout timer
						this.timeoutTimer.Reset();
					}

					// ...skip this check
					return;
				}

				// If the timeout timer is running, we found a match recently.  If we haven't found a match in more than
				// five seconds, it's not recent anymore, so reset our size to default.
				if (this.timeoutTimer.IsRunning && this.timeoutTimer.ElapsedMilliseconds > 5000)
				{
					verboseLog.AppendFormat("\nNo target detected within 5 seconds, timing out.");

					if (this.dockingModule.state != "Docked")
					{
						verboseLog.AppendFormat("\nReverting to default nodeType: {0}", this.defaultSize);
						this.currentSize = this.defaultSize;
					}

					this.timeoutTimer.Reset();
				}

				bool foundTargetNode = false;
				float closestNodeDistSqr = this.acquireRangeSqr * 4f;

				verboseLog.Append("Starting Vessels loop.");

				// Check all vessels for potential docking targets
				Vessel vessel;
				for (int vIdx = 0; vIdx < FlightGlobals.Vessels.Count; vIdx++)
				{
					vessel = FlightGlobals.Vessels[vIdx];
					if (vessel == null)
					{
						verboseLog.Append("Skipping null vessel.");
						continue;
					}
					
					// Skip vessels that are just way too far away.
					if (this.vessel.sqrDistanceTo(vessel) > this.vesselFilterDistanceSqr)
					{
						verboseLog.AppendFormat("\nSkipping distant vessel {0} (sqrDistance {1})",
							vessel.vesselName,
							(vessel.GetWorldPos3D() - this.dockingModule.nodeTransform.position).sqrMagnitude
						);
						continue;
					}

					verboseLog.AppendFormat("\nChecking nearby vessel {0} (sqrDistance {1})",
						vessel.vesselName,
						(vessel.GetWorldPos3D() - this.dockingModule.nodeTransform.position).sqrMagnitude
					);

					// Since this vessel is not too far away, check all docking nodes on the vessel.
					IList<ModuleDockingNode> potentialNodes = vessel.getModulesOfType<ModuleDockingNode>();
					ModuleDockingNode potentialTargetNode;
					for (int nIdx = 0; nIdx < potentialNodes.Count; nIdx++)
					{
						potentialTargetNode = potentialNodes[nIdx];

						if (potentialTargetNode == null)
						{
							verboseLog.AppendFormat("\nSkipping potentialTargetNode at index {0} because it is null",
								nIdx);
							continue;
						}

						if (potentialTargetNode.part == null)
						{
							verboseLog.AppendFormat("\nSkipping potentialTargetNode at index {0} because its part is null",
								nIdx);
							continue;
						}

						if (potentialTargetNode.nodeTransform == null)
						{
							verboseLog.AppendFormat("\nSkipping potentialTargetNode at index {0} because its node transform is null",
								nIdx);
							continue;
						}

						verboseLog.AppendFormat("\nFound potentialTargetNode: {0}", potentialTargetNode);
						verboseLog.AppendFormat("\n\tpotentialTargetNode.state: {0}", potentialTargetNode.state);
						verboseLog.AppendFormat("\n\tpotentialTargetNode.nodeType: {0}", potentialTargetNode.nodeType);

						// We can't skip the current vessel, because it's possible to dock parts on a single vessel to
						// each other.  Still, we can't dock a port to itself, so skip this part.
						if (potentialTargetNode.part == this.part)
						{
							verboseLog.AppendFormat("\nDiscarding potentialTargetNode: on this part.");
							continue;
						}

						// If this docking node is already docked, we can't dock to it, so skip it.
						if (
							potentialTargetNode.state.Contains("Docked") ||
							potentialTargetNode.state.Contains("PreAttached"))
						{
							verboseLog.Append("\nDiscarding potentialTargetNode: not ready.");
							continue;
						}

						float thisNodeDistSqr = 
							(potentialTargetNode.nodeTransform.position - this.dockingModule.nodeTransform.position)
								.sqrMagnitude;

						verboseLog.AppendFormat(
							"\n\tChecking potentialTargetNode sqrDistance against the lesser of acquireRangeSqr and " +
							"closestNodeDistSqr ({0})",
							Mathf.Min(this.acquireRangeSqr * 4f, closestNodeDistSqr)
						);

						// Only bother checking nodes closer than twice our acquire range.  We have to check before we
						// get within acquire range to make sure Squad's code will catch us when we get there.
						if (thisNodeDistSqr <= Mathf.Min(this.acquireRangeSqr * 4f, closestNodeDistSqr))
						{
							#if DEBUG
							foundApproach = true;
							#endif

							verboseLog.AppendFormat(
								"\n\tpotentialTargetNode is nearby ({0}), checking if adaptive.", thisNodeDistSqr);

							ModuleAdaptiveDockingNode targetAdaptiveNode = null;

							string targetSize;
							targetSize = string.Empty;

							// Only adapt to non-adaptive docking nodes if this node is gender neutral.
							if (
								this.validSizes.Contains(potentialTargetNode.nodeType) &&
								this.portGender == PortGender.NEUTRAL
							)
							{
								targetSize = potentialTargetNode.nodeType;
								this.currentSize = targetSize;
							}
							// Otherwise, look for another adaptive node.
							else
							{
								// Check the part for an AdaptiveDockingNode
								targetAdaptiveNode = potentialTargetNode.part
									.getFirstModuleOfType<ModuleAdaptiveDockingNode>();
							}

							// If we've found an AdaptiveDockingNode...
							if (targetAdaptiveNode != null)
							{
								// ...and if the genders don't match, skip this target.
								switch (this.portGender)
								{
									case PortGender.NEUTRAL:
										if (targetAdaptiveNode.portGender != PortGender.NEUTRAL)
											continue;
										break;
									case PortGender.FEMALE:
										if (targetAdaptiveNode.portGender != PortGender.MALE)
											continue;
										break;
									case PortGender.MALE:
										if (targetAdaptiveNode.portGender != PortGender.FEMALE)
											continue;
										break;
								}

								verboseLog.AppendFormat("\n\tpotentialTargetNode is adaptive.");
								verboseLog.AppendFormat("\n\tdefaultSize: {0}", targetAdaptiveNode.defaultSize);
								verboseLog.AppendFormat("\n\tvalidSizes: {0}", targetAdaptiveNode.validSizes);

								// ...and if we can become its largest (default) size...
								// This will never be true for gendered ports, because they will have _{gender} appended
								// to their default size.
								if (this.validSizes.Contains(targetAdaptiveNode.defaultSize))
								{
									// ...target its default size.
									targetSize = targetAdaptiveNode.defaultSize;
								}
								// ...otherwise, look for a common size.
								else
								{
									string commonNodeType = GetGreatestCommonNodeType(this, targetAdaptiveNode);

									// ...if we didn't find a common size, stop processing this node
									if (commonNodeType == string.Empty)
									{
										verboseLog.AppendFormat("\n\tInvalid adaptive target: no common node types.");
										continue;
									}

									// ...otherwise, target the common size, obfuscating for gendered ports just in case
									switch (this.portGender)
									{
										case PortGender.FEMALE:
										case PortGender.MALE:
											targetSize = String.Concat(commonNodeType, "_gendered");
											break;
										default:
											targetSize = commonNodeType;
											break;
									}

									targetAdaptiveNode.currentSize = targetSize;

									verboseLog.AppendFormat(
										"\n\tTarget nodeType set to commonNodeType: {0}", targetSize);
								}
							}
							#if DEBUG
							else
							{
								verboseLog.AppendFormat("\n\tpotentialTargetNode is not adaptive.");
								verboseLog.AppendFormat("\n\tnodeType: {0}", potentialTargetNode.nodeType);
							}
							#endif

							// If we never found a target size, it's not a match, so stop processing.
							if (targetSize == string.Empty)
							{
								continue;
							}

							// ...otherwise, log this node as the closest and adapt to the target size.
							closestNodeDistSqr = thisNodeDistSqr;
							this.currentSize = targetSize;
							foundTargetNode = true;


							verboseLog.AppendFormat("\n\tLocal nodeType set to commonNodeType: {0}", targetSize);
							verboseLog.AppendFormat("\n\tFound suitable docking node.");
							verboseLog.AppendFormat("\n\ttargetSize: {0}", targetSize);
						
							verboseLog.AppendFormat("\n\tForward vector dot product: {0} (acquire minimum: {1})",
								Vector3.Dot(potentialTargetNode.nodeTransform.forward,
									this.dockingModule.nodeTransform.forward),
								this.dockingModule.acquireMinFwdDot
							);

							verboseLog.AppendFormat("\n\tUp vector dot product: {0} (acquire minimum: {1})",
								Vector3.Dot(potentialTargetNode.nodeTransform.up, this.dockingModule.nodeTransform.up),
								this.dockingModule.acquireMinRollDot
							);
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

				if (foundTargetNode)
				{
					if (this.timeoutTimer.IsRunning)
					{
						this.timeoutTimer.Reset();
					}

					this.timeoutTimer.Start();
				}

				#if DEBUG
				}
				finally
				{
					// if (foundApproach)
					verboseLog.Print();
				}
				#endif
			}
		}

		public void LateUpdate()
		{
			if (HighLogic.LoadedSceneIsEditor)
			{
				Logging.PostDebugMessage(this, "LateUpdate", string.Format("attachedPart: {0}", this.attachedPart));

				if (this.hasAttachedPart != this.hasAttachedState)
				{
					this.hasAttachedState = this.hasAttachedPart;

					if (this.attachedPart != null)
					{
						ModuleDockingNode attachedNode = this.attachedPart.getFirstModuleOfType<ModuleDockingNode>();

						Logging.PostDebugMessage(this, string.Format("attachedNode: {0}", attachedNode));

						if (attachedNode != null)
						{
							ModuleAdaptiveDockingNode attachedADN = this.attachedPart
								.getFirstModuleOfType<ModuleAdaptiveDockingNode>();

							if (attachedADN != null && attachedADN.currentSize != this.currentSize)
							{
								this.currentSize = GetGreatestCommonNodeType(this, attachedADN);

								Logging.PostDebugMessage(this,
									string.Format("Attached to AdaptiveDockingNode, setting currentSize = {0}",
										this.currentSize)
								);
							}
							else if (attachedNode.nodeType != this.currentSize)
							{
								this.currentSize = attachedNode.nodeType;

								Logging.PostDebugMessage(this,
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

	public enum PortGender
	{
		NEUTRAL,
		MALE,
		FEMALE
	}
}
