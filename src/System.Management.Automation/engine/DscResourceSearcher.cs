/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation.Runspaces;
using Dbg = System.Management.Automation.Diagnostics;

namespace System.Management.Automation
{
    /// <summary>
    /// Searcher class for finding DscResources on the system.
    /// </summary>
    internal class DscResourceSearcher : IEnumerable<DscResourceInfo>, IEnumerator<DscResourceInfo>
    {
        internal DscResourceSearcher(
            string resourceName,
            ExecutionContext context)
        {
            Diagnostics.Assert(context != null, "caller to verify context is not null");
            Diagnostics.Assert(!string.IsNullOrEmpty(resourceName), "caller to verify commandName is valid");

            this._resourceName = resourceName;
            this._context = context;
        }

        #region private properties

        private string _resourceName = null;
        private ExecutionContext _context = null;
        private DscResourceInfo _currentMatch = null;
        private IEnumerator<DscResourceInfo> matchingResource = null;
        private Collection<DscResourceInfo> matchingResourceList = null;

        #endregion

        #region public methods

        /// <summary>
        /// Reset the Iterator.
        /// </summary>
        public void Reset()
        {
            _currentMatch = null;
            matchingResource = null;
        }

        /// <summary>
        /// Reset and dispose the Iterator.
        /// </summary>
        public void Dispose()
        {
            Reset();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Get the Enumerator.
        /// </summary>
        /// <returns></returns>
        IEnumerator<DscResourceInfo> IEnumerable<DscResourceInfo>.GetEnumerator()
        {
            return this;
        }

        /// <summary>
        /// Get the Enumerator
        /// </summary>
        /// <returns></returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this;
        }

        /// <summary>
        /// Move to the Next value in the enumerator. 
        /// </summary>
        /// <returns></returns>
        public bool MoveNext()
        {
            _currentMatch = GetNextDscResource();

            if (_currentMatch != null)
                return true;

            return false;
        }

        /// <summary>
        /// Return the current DscResource
        /// </summary>
        DscResourceInfo IEnumerator<DscResourceInfo>.Current
        {
            get
            {
                return _currentMatch;
            }
        }

        /// <summary>
        /// Return the current DscResource as object
        /// </summary>
        object IEnumerator.Current
        {
            get
            {
                return ((IEnumerator<DscResourceInfo>)this).Current;
            }
        }

        #endregion

        #region private methods

        /// <summary>
        /// Invoke command Get-DscResource with resource name to find the resource.
        /// When found add them to the enumerator. If we have already got it, return the next resource.
        /// </summary>
        /// <returns>Next DscResource Info object or null if none are found.</returns>
        private DscResourceInfo GetNextDscResource()
        {
            DscResourceInfo returnValue = null;

            var ps = PowerShell.Create(RunspaceMode.CurrentRunspace).AddCommand("Get-DscResource");

            WildcardPattern resourceMatcher = WildcardPattern.Get(_resourceName, WildcardOptions.IgnoreCase);

            if (matchingResourceList == null)
            {
                Collection<PSObject> psObjs = ps.Invoke();

                matchingResourceList = new Collection<DscResourceInfo>();

                bool matchFound = false;

                foreach (dynamic resource in psObjs)
                {
                    if (resource.Name != null)
                    {
                        string resourceName = resource.Name;

                        if (resourceMatcher.IsMatch(resourceName))
                        {
                            DscResourceInfo resourceInfo = new DscResourceInfo(resourceName,
                                                                               resource.ResourceType,                                                                               
                                                                               resource.Path,
                                                                               resource.ParentPath,
                                                                               _context
                                                                               );

                            
                            resourceInfo.FriendlyName = resource.FriendlyName;                            

                            resourceInfo.CompanyName = resource.CompanyName;

                            PSModuleInfo psMod = resource.Module as PSModuleInfo;

                            if (psMod != null)
                                resourceInfo.Module = psMod;
                            
                            if (resource.ImplementedAs != null)
                            {
                                ImplementedAsType impType;
                                if (Enum.TryParse<ImplementedAsType>(resource.ImplementedAs.ToString(), out impType))
                                    resourceInfo.ImplementedAs = impType;
                            }

                            var properties = resource.Properties as IList;

                            if (properties != null)
                            {
                                List<DscResourcePropertyInfo> propertyList = new List<DscResourcePropertyInfo>();

                                foreach (dynamic prop in properties)
                                {
                                    DscResourcePropertyInfo propInfo = new DscResourcePropertyInfo();
                                    propInfo.Name = prop.Name;
                                    propInfo.PropertyType = prop.PropertyType;
                                    propInfo.UpdateValues(prop.Values);

                                    propertyList.Add(propInfo);
                                }

                                resourceInfo.UpdateProperties(propertyList);
                            }
                            
                            matchingResourceList.Add(resourceInfo);

                            matchFound = true;
                        } //if 
                    }//if
                }// foreach

                if (matchFound)
                    matchingResource = matchingResourceList.GetEnumerator();
                else
                    return returnValue;
            }//if

            if (!matchingResource.MoveNext())
            {
                matchingResource = null;
            }
            else
            {
                returnValue = matchingResource.Current;
            }

            return returnValue;
        }

        #endregion

    }
}
