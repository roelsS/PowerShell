/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.PowerShell;
using Dbg = System.Diagnostics.Debug;
using System.Management.Automation.Language;

namespace System.Management.Automation
{
    /// <summary>
    /// This class represents the compiled metadata for a parameter set.
    /// </summary>
    public sealed class ParameterSetMetadata
    {
        #region Private Data

        private bool isMandatory;
        private int position;
        private bool valueFromPipeline;
        private bool valueFromPipelineByPropertyName;
        private bool valueFromRemainingArguments;
        private string helpMessage;
        private string helpMessageBaseName;
        private string helpMessageResourceId;

        #endregion

        #region Constructor

        /// <summary>
        /// 
        /// </summary>
        /// <param name="psMD"></param>
        internal ParameterSetMetadata(ParameterSetSpecificMetadata psMD)
        {
            Dbg.Assert(null != psMD, "ParameterSetSpecificMetadata cannot be null");
            Initialize(psMD);
        }

        /// <summary>
        /// A copy constructor that creates a deep copy of the <paramref name="other"/> ParameterSetMetadata object.
        /// </summary>
        /// <param name="other">object to copy</param>
        internal ParameterSetMetadata(ParameterSetMetadata other)
        {
            if (other == null)
            {
                throw PSTraceSource.NewArgumentNullException("other");
            }

            this.helpMessage = other.helpMessage;
            this.helpMessageBaseName = other.helpMessageBaseName;
            this.helpMessageResourceId = other.helpMessageResourceId;
            this.isMandatory = other.isMandatory;
            this.position = other.position;
            this.valueFromPipeline = other.valueFromPipeline;
            this.valueFromPipelineByPropertyName = other.valueFromPipelineByPropertyName;
            this.valueFromRemainingArguments = other.valueFromRemainingArguments;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Returns true if the parameter is mandatory for this parameterset, false otherwise.
        /// </summary>
        /// <value></value>
        public bool IsMandatory
        {
            get
            {
                return isMandatory;
            }

            set
            {
                isMandatory = value;
            }
        }

        /// <summary>
        /// If the parameter is allowed to be positional for this parameter set, this returns
        /// the position it is allowed to be in. If it is not positional, this returns int.MinValue.
        /// </summary>
        /// <value></value>
        public int Position
        {
            get
            {
                return position;
            }
            set
            {
                position = value;
            }
        }

        /// <summary>
        /// Specifies that this parameter can take values from the incoming pipeline object.
        /// </summary>
        public bool ValueFromPipeline
        {
            get
            {
                return valueFromPipeline;
            }
            set
            {
                valueFromPipeline = value;
            }
        }

        /// <summary>
        /// Specifies that this parameter can take values from a property from the incoming
        /// pipeline object with the same name as the parameter.
        /// </summary>
        public bool ValueFromPipelineByPropertyName
        {
            get
            {
                return valueFromPipelineByPropertyName;
            }
            set
            {
                valueFromPipelineByPropertyName = value;
            }
        }

        /// <summary>
        /// Specifies if this parameter takes all the remaining unbound 
        /// arguments that were specified
        /// </summary>
        /// <value></value>
        public bool ValueFromRemainingArguments
        {
            get
            {
                return valueFromRemainingArguments;
            }
            set
            {
                valueFromRemainingArguments = value;
            }
        }

        /// <summary>
        /// A short description for this parameter, suitable for presentation as a tool tip.
        /// </summary>
        public string HelpMessage
        {
            get
            {
                return helpMessage;
            }
            set
            {
                helpMessage = value;
            }
        }

        /// <summary>
        /// The base name of the resource for a help message.
        /// </summary>
        public string HelpMessageBaseName
        {
            get
            {
                return helpMessageBaseName;
            }
            set
            {
                helpMessageBaseName = value;
            }
        }

        /// <summary>
        /// The Id of the resource for a help message.
        /// </summary>
        public string HelpMessageResourceId
        {
            get
            {
                return helpMessageResourceId;
            }
            set
            {
                helpMessageResourceId = value;
            }
        }       

        #endregion

        #region Private / Internal Methods & Properties

        /// <summary>
        /// 
        /// </summary>
        /// <param name="psMD"></param>
        internal void Initialize(ParameterSetSpecificMetadata psMD)
        {
            isMandatory = psMD.IsMandatory;
            position = psMD.Position;
            valueFromPipeline = psMD.ValueFromPipeline;
            valueFromPipelineByPropertyName = psMD.ValueFromPipelineByPropertyName;
            valueFromRemainingArguments = psMD.ValueFromRemainingArguments;            
            helpMessage = psMD.HelpMessage;
            helpMessageBaseName = psMD.HelpMessageBaseName;
            helpMessageResourceId = psMD.HelpMessageResourceId;
        }
        
        /// <summary>
        /// Compares this instance with the supplied <paramref name="second"/>.
        /// </summary>
        /// <param name="second">
        /// An object to compare this instance with
        /// </param>
        /// <returns>
        /// true if the metadata is same. false otherwise.
        /// </returns>
        internal bool Equals(ParameterSetMetadata second)
        {
            if ((isMandatory != second.isMandatory) ||
                (position != second.position) ||
                (valueFromPipeline != second.valueFromPipeline) ||
                (valueFromPipelineByPropertyName != second.valueFromPipelineByPropertyName) ||
                (valueFromRemainingArguments != second.valueFromRemainingArguments) ||
                (helpMessage != second.helpMessage) ||
                (helpMessageBaseName != second.helpMessageBaseName) ||
                (helpMessageResourceId != second.helpMessageResourceId))
            {
                return false;
            }

            return true;
        }

        #endregion

        #region Efficient serialization + rehydration logic

        [Flags]
        internal enum ParameterFlags : uint
        {
            Mandatory = 0x01,
            ValueFromPipeline = 0x02,
            ValueFromPipelineByPropertyName = 0x04,
            ValueFromRemainingArguments = 0x08,
        }

        internal ParameterFlags Flags
        {
            get
            {
                ParameterFlags flags = 0;
                if (IsMandatory) { flags = flags | ParameterFlags.Mandatory; }
                if (ValueFromPipeline) { flags = flags | ParameterFlags.ValueFromPipeline; }
                if (ValueFromPipelineByPropertyName) { flags = flags | ParameterFlags.ValueFromPipelineByPropertyName; }
                if (ValueFromRemainingArguments) { flags = flags | ParameterFlags.ValueFromRemainingArguments; }
                return flags;
            }
            set
            {
                this.IsMandatory = (ParameterFlags.Mandatory == (value & ParameterFlags.Mandatory));
                this.ValueFromPipeline = (ParameterFlags.ValueFromPipeline == (value & ParameterFlags.ValueFromPipeline));
                this.ValueFromPipelineByPropertyName = (ParameterFlags.ValueFromPipelineByPropertyName == (value & ParameterFlags.ValueFromPipelineByPropertyName));
                this.ValueFromRemainingArguments = (ParameterFlags.ValueFromRemainingArguments == (value & ParameterFlags.ValueFromRemainingArguments));
            }
        }

        /// <summary>
        /// Constructor used by rehydration
        /// </summary>
        internal ParameterSetMetadata(
            int position,
            ParameterFlags flags,
            string helpMessage)
        {
            this.Position = position;
            this.Flags = flags;
            this.HelpMessage = helpMessage;
        }

        #endregion

        #region Proxy Parameter Generation

        private const string MandatoryFormat = @"{0}Mandatory=$true";
        private const string PositionFormat = @"{0}Position={1}";
        private const string ValueFromPipelineFormat = @"{0}ValueFromPipeline=$true";
        private const string ValueFromPipelineByPropertyNameFormat = @"{0}ValueFromPipelineByPropertyName=$true";
        private const string ValueFromRemainingArgumentsFormat = @"{0}ValueFromRemainingArguments=$true";
        private const string HelpMessageFormat = @"{0}HelpMessage='{1}'";

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        internal string GetProxyParameterData()
        {
            Text.StringBuilder result = new System.Text.StringBuilder();
            string prefix = "";

            if (isMandatory)
            {
                result.AppendFormat(CultureInfo.InvariantCulture, MandatoryFormat, prefix);
                prefix = ", ";
            }

            if (position != Int32.MinValue)
            {
                result.AppendFormat(CultureInfo.InvariantCulture, PositionFormat, prefix, position);
                prefix = ", ";
            }

            if (valueFromPipeline)
            {
                result.AppendFormat(CultureInfo.InvariantCulture, ValueFromPipelineFormat, prefix);
                prefix = ", ";
            }

            if (valueFromPipelineByPropertyName)
            {
                result.AppendFormat(CultureInfo.InvariantCulture, ValueFromPipelineByPropertyNameFormat, prefix);
                prefix = ", ";
            }

            if (valueFromRemainingArguments)
            {
                result.AppendFormat(CultureInfo.InvariantCulture, ValueFromRemainingArgumentsFormat, prefix);
                prefix = ", ";
            }

            if (!string.IsNullOrEmpty(helpMessage))
            {
                result.AppendFormat(
                    CultureInfo.InvariantCulture,
                    HelpMessageFormat, 
                    prefix, 
                    CodeGeneration.EscapeSingleQuotedStringContent(helpMessage));
                prefix = ", ";
            }

            return result.ToString();
        }

        #endregion
    }

    /// <summary>
    /// This class represents the compiled metadata for a parameter.
    /// </summary>
    public sealed class ParameterMetadata
    {
        #region Private Data

        private string name;
        private Type parameterType;
        private bool isDynamic;
        private Dictionary<string, ParameterSetMetadata> parameterSets;
        private Collection<string> aliases;
        private Collection<Attribute> attributes;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructs a ParameterMetadata instance.
        /// </summary>
        /// <param name="name">
        /// Name of the parameter.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// name is null.
        /// </exception>
        public ParameterMetadata(string name)
            : this(name, null)
        {
        }

        /// <summary>
        /// Constructs a ParameterMetadata instance.
        /// </summary>
        /// <param name="name">
        /// Name of the parameter.
        /// </param>
        /// <param name="parameterType">
        /// Type of the parameter.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// name is null.
        /// </exception>
        public ParameterMetadata(string name, Type parameterType)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentNullException("name");
            }

            this.name = name;
            this.parameterType = parameterType;

            this.attributes = new Collection<Attribute>();
            this.aliases = new Collection<string>();
            this.parameterSets = new Dictionary<string, ParameterSetMetadata>();
        }

        /// <summary>
        /// A copy constructor that creates a deep copy of the <paramref name="other"/> ParameterMetadata object.
        /// Instances of Attribute and Type classes are copied by reference.
        /// </summary>
        /// <param name="other">object to copy</param>
        public ParameterMetadata(ParameterMetadata other)
        {
            if (other == null)
            {
                throw PSTraceSource.NewArgumentNullException("other");
            }

            this.isDynamic = other.isDynamic;
            this.name = other.name;
            this.parameterType = other.parameterType;

            // deep copy
            this.aliases = new Collection<string>(new List<string>(other.aliases.Count));
            foreach (string alias in other.aliases)
            {
                this.aliases.Add(alias);
            }

            // deep copy of the collection, collection items (Attributes) copied by reference
            if (other.attributes == null)
            {
                this.attributes = null;
            }
            else
            {
                this.attributes = new Collection<Attribute>(new List<Attribute>(other.attributes.Count));
                foreach (Attribute attribute in other.attributes)
                {
                    this.attributes.Add(attribute);
                }
            }

            // deep copy
            this.parameterSets = null;
            if (other.parameterSets == null)
            {
                this.parameterSets = null;
            }
            else
            {
                this.parameterSets = new Dictionary<string, ParameterSetMetadata>(other.parameterSets.Count);
                foreach (KeyValuePair<string, ParameterSetMetadata> entry in other.parameterSets)
                {
                    this.parameterSets.Add(entry.Key, new ParameterSetMetadata(entry.Value));
                }
            }
        }

        /// <summary>
        /// An internal constructor which constructs a ParameterMetadata object
        /// from compiled commmand parameter metadata. ParameterMetadata
        /// is a proxy written on top of CompiledCommandParameter
        /// </summary>
        /// <param name="cmdParameterMD">
        /// Internal CompiledCommandParameter metadata
        /// </param>
        internal ParameterMetadata(CompiledCommandParameter cmdParameterMD)
        {
            Dbg.Assert(null != cmdParameterMD,
                "CompiledCommandParameter cannot be null");

            Initialize(cmdParameterMD);
        }

        /// <summary>
        /// Constructor used by implicit remoting
        /// </summary>
        internal ParameterMetadata(
            Collection<string> aliases,
            bool isDynamic,
            string name,
            Dictionary<string, ParameterSetMetadata> parameterSets,
            Type parameterType)
        {
            this.aliases = aliases;
            this.isDynamic = isDynamic;
            this.name = name;
            this.parameterSets = parameterSets;
            this.parameterType = parameterType;
            this.attributes = new Collection<Attribute>();
        }

        #endregion

        #region Public Methods/Properties
        
        /// <summary>
        /// Gets the name of the parameter
        /// </summary>
        /// 
        public String Name
        {
            get
            {
                return name;
            }

            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw PSTraceSource.NewArgumentNullException("Name");
                }

                name = value;
            }
        }

        /// <summary>
        /// Gets the Type information of the Parameter.
        /// </summary>
        public Type ParameterType
        {
            get
            {
                return parameterType;
            }

            set
            {
                parameterType = value;
            }
        }

        /// <summary>
        /// Gets the ParameterSets metadata that this parameter belongs to.
        /// </summary>
        public Dictionary<string, ParameterSetMetadata> ParameterSets
        {
            get
            {
                return parameterSets;
            }
        }

        /// <summary>
        /// Specifies if the parameter is Dynamic
        /// </summary>
        public bool IsDynamic 
        {
            get { return isDynamic; }
            set { isDynamic = value; }
        }
        /// <summary>
        /// Specifies the alias names for this parameter
        /// </summary>
        public Collection<string> Aliases 
        {
            get
            {
                return aliases;
            }
        }

        /// <summary>
        /// A collection of the attributes found on the member.
        /// </summary>
        public Collection<Attribute> Attributes
        {
            get
            {
                return attributes;
            }
        }

        /// <summary>
        /// Specifies if the parameter is a SwitchParameter
        /// </summary>
        public bool SwitchParameter 
        {
            get
            {
                if (parameterType != null)
                {
                    return parameterType.Equals(typeof(SwitchParameter));
                }

                return false;
            }
        }
        
        /// <summary>
        /// Gets a dictionary of parameter metadata for the supplied <paramref name="type"/>.  
        /// </summary>
        /// <param name="type">
        /// CLR Type for which the parameter metadata is constructed.
        /// </param>
        /// <returns>
        /// A Dictionary of ParameterMetadata keyed by parameter name.
        /// null if no parameter metadata is found.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// type is null.
        /// </exception>
        public static Dictionary<string, ParameterMetadata> GetParameterMetadata(Type type)
        {
            if (null == type)
            {
                throw PSTraceSource.NewArgumentNullException("type");
            }

            CommandMetadata cmdMetaData = new CommandMetadata(type);
            Dictionary<string, ParameterMetadata> result = cmdMetaData.Parameters;
            // early GC.
            cmdMetaData = null;
            return result;
        }

        #endregion

        #region Internal Methods/Properties

        /// <summary>
        /// 
        /// </summary>
        /// <param name="compiledParameterMD"></param>
        internal void Initialize(CompiledCommandParameter compiledParameterMD)
        {
            name = compiledParameterMD.Name;
            parameterType = compiledParameterMD.Type;
            isDynamic = compiledParameterMD.IsDynamic;
            
            // Create parameter set metadata
            parameterSets = new Dictionary<string, ParameterSetMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in compiledParameterMD.ParameterSetData.Keys)
            {
                ParameterSetSpecificMetadata pMD = compiledParameterMD.ParameterSetData[key];
                parameterSets.Add(key, new ParameterSetMetadata(pMD));
            }

            // Create aliases for this parameter
            aliases = new Collection<string>();
            foreach (string alias in compiledParameterMD.Aliases)
            {
                aliases.Add(alias);
            }

            // Create attributes for this parameter
            attributes = new Collection<Attribute>();
            foreach (var attrib in compiledParameterMD.CompiledAttributes)
            {
                attributes.Add(attrib);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cmdParameterMetadata"></param>
        /// <returns></returns>
        internal static Dictionary<string, ParameterMetadata> GetParameterMetadata(MergedCommandParameterMetadata
            cmdParameterMetadata)
        {
            Dbg.Assert(null != cmdParameterMetadata, "cmdParameterMetadata cannot be null");

            Dictionary<string, ParameterMetadata> result = new Dictionary<string, ParameterMetadata>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var keyValuePair in cmdParameterMetadata.BindableParameters)
            {
                var key = keyValuePair.Key;
                var mergedCompiledPMD = keyValuePair.Value;
                ParameterMetadata parameterMetaData = new ParameterMetadata(mergedCompiledPMD.Parameter);
                result.Add(key, parameterMetaData);
            }

            return result;
        }

        internal bool IsMatchingType(PSTypeName psTypeName)
        {
            Type dotNetType = psTypeName.Type;
            if (dotNetType != null)
            {
                // ConstrainedLanguage note - This conversion is analyzed, but actually invoked via regular conversion.
                bool parameterAcceptsObjects =
                    ((int) (LanguagePrimitives.FigureConversion(typeof (object), this.ParameterType).Rank)) >=
                    (int) (ConversionRank.AssignableS2A);
                if (dotNetType.Equals(typeof(object)))
                {
                    return parameterAcceptsObjects;
                }
                if (parameterAcceptsObjects)
                {
                    return (psTypeName.Type != null) && (psTypeName.Type.Equals(typeof (object)));
                }

                // ConstrainedLanguage note - This conversion is analyzed, but actually invoked via regular conversion.
                var convertionData = LanguagePrimitives.FigureConversion(dotNetType, this.ParameterType);
                if (convertionData != null)
                {
                    if ((int) (convertionData.Rank) >= (int) (ConversionRank.NumericImplicitS2A))
                    {
                        return true;
                    }
                }

                return false;
            }

            var wildcardPattern = WildcardPattern.Get(
                "*" + (psTypeName.Name ?? ""),
                WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant);
            if (wildcardPattern.IsMatch(this.ParameterType.FullName))
            {
                return true;
            }
            if (this.ParameterType.IsArray && wildcardPattern.IsMatch((this.ParameterType.GetElementType().FullName)))
            {
                return true;
            }
            if (this.Attributes != null)
            {
                PSTypeNameAttribute typeNameAttribute = this.Attributes.OfType<PSTypeNameAttribute>().FirstOrDefault();
                if (typeNameAttribute != null && wildcardPattern.IsMatch(typeNameAttribute.PSTypeName))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Proxy Parameter generation

        // The formats are prefixed with {0} to enable easy formatting.
        private const string ParameterNameFormat = @"{0}${{{1}}}";
        private const string ParameterTypeFormat = @"{0}[{1}]";
        private const string ParameterSetNameFormat = "ParameterSetName='{0}'";
        private const string AliasesFormat = @"{0}[Alias({1})]";
        private const string ValidateLengthFormat = @"{0}[ValidateLength({1}, {2})]";
        private const string ValidateRangeFloatFormat = @"{0}[ValidateRange({1:R}, {2:R})]";
        private const string ValidateRangeFormat = @"{0}[ValidateRange({1}, {2})]";
        private const string ValidatePatternFormat = "{0}[ValidatePattern('{1}')]";
        private const string ValidateScriptFormat = @"{0}[ValidateScript({{ {1} }})]";
        private const string ValidateCountFormat = @"{0}[ValidateCount({1}, {2})]";
        private const string ValidateSetFormat = @"{0}[ValidateSet({1})]";
        private const string ValidateNotNullFormat = @"{0}[ValidateNotNull()]";
        private const string ValidateNotNullOrEmptyFormat = @"{0}[ValidateNotNullOrEmpty()]";
        private const string AllowNullFormat = @"{0}[AllowNull()]";
        private const string AllowEmptyStringFormat = @"{0}[AllowEmptyString()]";
        private const string AllowEmptyCollectionFormat = @"{0}[AllowEmptyCollection()]";
        private const string PSTypeNameFormat = @"{0}[PSTypeName('{1}')]";
        private const string ObsoleteFormat = @"{0}[Obsolete({1})]";
        private const string CredentialAttributeFormat = @"{0}[System.Management.Automation.CredentialAttribute()]";

        /// <summary>
        /// 
        /// </summary>
        /// <param name="prefix">
        /// prefix that is added to every new-line. Used for tabbing content.
        /// </param>
        /// <param name="paramNameOverride">
        /// The paramNameOverride is used as the parameter name if it is not null or empty.
        /// </param>
        /// <param name="isProxyForCmdlet">
        /// The parameter is for a cmdlet and requires a Parameter attribute.
        /// </param>
        /// <returns></returns>
        internal string GetProxyParameterData(string prefix, string paramNameOverride, bool isProxyForCmdlet)
        {
            Text.StringBuilder result = new System.Text.StringBuilder();

            if (parameterSets != null && isProxyForCmdlet)
            {
                foreach (var pair in parameterSets)
                {
                    string parameterSetName = pair.Key;
                    ParameterSetMetadata parameterSet = pair.Value;
                    string paramSetData = parameterSet.GetProxyParameterData();
                    if (!string.IsNullOrEmpty(paramSetData) || !parameterSetName.Equals(ParameterAttribute.AllParameterSets))
                    {
                        string separator = "";
                        result.Append(prefix);
                        result.Append("[Parameter(");
                        if (!parameterSetName.Equals(ParameterAttribute.AllParameterSets))
                        {
                            result.AppendFormat(
                                CultureInfo.InvariantCulture,
                                ParameterSetNameFormat, 
                                CodeGeneration.EscapeSingleQuotedStringContent(parameterSetName));
                            separator = ", ";
                        }
                        if (!string.IsNullOrEmpty(paramSetData))
                        {
                            result.Append(separator);
                            result.Append(paramSetData);
                        }
                        result.Append(")]");
                    }
                }
            }

            if ((aliases != null) && (aliases.Count > 0))
            {
                Text.StringBuilder aliasesData = new System.Text.StringBuilder();
                string comma = ""; // comma is not need for the first element

                foreach (string alias in aliases)
                {
                    aliasesData.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0}'{1}'",
                        comma,
                        CodeGeneration.EscapeSingleQuotedStringContent(alias));
                    comma = ",";
                }

                result.AppendFormat(CultureInfo.InvariantCulture, AliasesFormat, prefix, aliasesData.ToString());
            }

            if ((attributes != null) && (attributes.Count > 0))
            {
                foreach (Attribute attrib in attributes)
                {
                    string attribData = GetProxyAttributeData(attrib, prefix);
                    if (!string.IsNullOrEmpty(attribData))
                    {
                        result.Append(attribData);
                    }
                }
            }

            if (SwitchParameter)
            {
                result.AppendFormat(CultureInfo.InvariantCulture, ParameterTypeFormat, prefix, "switch");
            }
            else if (parameterType != null)
            {
                result.AppendFormat(CultureInfo.InvariantCulture, ParameterTypeFormat, prefix, ToStringCodeMethods.Type(parameterType));
            }

            /* 1. CredentialAttribute needs to go after the type
             * 2. To avoid risk, I don't want to move other attributes to go here / after the type */
            CredentialAttribute credentialAttrib = attributes.OfType<CredentialAttribute>().FirstOrDefault();
            if (credentialAttrib != null)
            {
                string attribData = string.Format(CultureInfo.InvariantCulture, CredentialAttributeFormat, prefix);
                if (!string.IsNullOrEmpty(attribData))
                {
                    result.Append(attribData);
                }
            }

            result.AppendFormat(
                CultureInfo.InvariantCulture,
                ParameterNameFormat, 
                prefix,
                CodeGeneration.EscapeVariableName(string.IsNullOrEmpty(paramNameOverride) ? name : paramNameOverride));
            return result.ToString();
        }

        /// <summary>
        /// Generates proxy data for attributes like ValidateLength, ValidateRange etc.
        /// </summary>
        /// <param name="attrib">
        /// Attribute to process.
        /// </param>
        /// <param name="prefix">
        /// Prefix string to add.
        /// </param>
        /// <returns>
        /// Attribute's proxy string.
        /// </returns>
        private string GetProxyAttributeData(Attribute attrib, string prefix)
        {
            string result;

            ValidateLengthAttribute validLengthAttrib = attrib as ValidateLengthAttribute;
            if (validLengthAttrib != null)
            {
                result = string.Format(CultureInfo.InvariantCulture,
                    ValidateLengthFormat, prefix,
                    validLengthAttrib.MinLength, validLengthAttrib.MaxLength);
                return result;
            }

            ValidateRangeAttribute validRangeAttrib = attrib as ValidateRangeAttribute;
            if (validRangeAttrib != null)
            {
                Type rangeType = validRangeAttrib.MinRange.GetType();
                string format;

                if (rangeType == typeof(float) || rangeType == typeof(double))
                {
                    format = ValidateRangeFloatFormat;
                }
                else
                {
                    format = ValidateRangeFormat;
                }
                result = string.Format(CultureInfo.InvariantCulture,
                    format, prefix,
                    validRangeAttrib.MinRange, validRangeAttrib.MaxRange);
                return result;
            }

            AllowNullAttribute allowNullAttrib = attrib as AllowNullAttribute;
            if (allowNullAttrib != null)
            {
                result = string.Format(CultureInfo.InvariantCulture,
                    AllowNullFormat, prefix);
                return result;
            }

            AllowEmptyStringAttribute allowEmptyStringAttrib = attrib as AllowEmptyStringAttribute;
            if (allowEmptyStringAttrib != null)
            {
                result = string.Format(CultureInfo.InvariantCulture,
                    AllowEmptyStringFormat, prefix);
                return result;
            }

            AllowEmptyCollectionAttribute allowEmptyColAttrib = attrib as AllowEmptyCollectionAttribute;
            if (allowEmptyColAttrib != null)
            {
                result = string.Format(CultureInfo.InvariantCulture,
                    AllowEmptyCollectionFormat, prefix);
                return result;
            }

            ValidatePatternAttribute patternAttrib = attrib as ValidatePatternAttribute;
            if (patternAttrib != null)
            {
                /* TODO: Validate Pattern dont support Options in ScriptCmdletText.
                StringBuilder regexOps = new System.Text.StringBuilder();
                string or = "";
                string[] regexOptionEnumValues = Enum.GetNames(typeof(System.Text.RegularExpressions.RegexOptions));
                  
                foreach(string regexOption in regexOptionEnumValues)
                {
                    System.Text.RegularExpressions.RegexOptions option = (System.Text.RegularExpressions.RegexOptions) Enum.Parse(
                        typeof(System.Text.RegularExpressions.RegexOptions),
                        regexOption, true);
                    if ((option & patternAttrib.Options) == option)
                    {
                        tracer.WriteLine("Regex option {0} found", regexOption);
                        regexOps.AppendFormat(CultureInfo.InvariantCulture,
                            "{0}[System.Text.RegularExpressions.RegexOptions]::{1}", or,
                            option.ToString()
                            );
                        or = "|";
                    }
                }*/

                result = string.Format(CultureInfo.InvariantCulture,
                    ValidatePatternFormat, prefix,
                    CodeGeneration.EscapeSingleQuotedStringContent(patternAttrib.RegexPattern)
                    /*,regexOps.ToString()*/);
                return result;
            }

            ValidateCountAttribute countAttrib = attrib as ValidateCountAttribute;
            if (countAttrib != null)
            {
                result = string.Format(CultureInfo.InvariantCulture,
                    ValidateCountFormat, prefix, countAttrib.MinLength, countAttrib.MaxLength);
                return result;
            }

            ValidateNotNullAttribute notNullAttrib = attrib as ValidateNotNullAttribute;
            if (notNullAttrib != null)
            {
                result = string.Format(CultureInfo.InvariantCulture,
                    ValidateNotNullFormat, prefix);
                return result;
            }

            ValidateNotNullOrEmptyAttribute notNullEmptyAttrib = attrib as ValidateNotNullOrEmptyAttribute;
            if (notNullEmptyAttrib != null)
            {
                result = string.Format(CultureInfo.InvariantCulture,
                    ValidateNotNullOrEmptyFormat, prefix);
                return result;
            }

            ValidateSetAttribute setAttrib = attrib as ValidateSetAttribute;
            if (setAttrib != null)
            {
                Text.StringBuilder values = new System.Text.StringBuilder();
                string comma = "";
                foreach(string validValue in setAttrib.ValidValues)
                {
                    values.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0}'{1}'",
                        comma,
                        CodeGeneration.EscapeSingleQuotedStringContent(validValue));
                    comma = ",";
                }

                result = string.Format(CultureInfo.InvariantCulture,
                    ValidateSetFormat, prefix, values.ToString()/*, setAttrib.IgnoreCase*/);
                return result;
            }

            ValidateScriptAttribute scriptAttrib = attrib as ValidateScriptAttribute;
            if (scriptAttrib != null)
            {
                // Talked with others and I think it is okay to use *unescaped* value from sb.ToString()
                // 1. implicit remoting is not bringing validation scripts across
                // 2. other places in code also assume that contents of a script block can be parsed
                //    without escaping
                result = string.Format(CultureInfo.InvariantCulture,
                    ValidateScriptFormat, prefix, scriptAttrib.ScriptBlock.ToString());
                return result;
            }

            PSTypeNameAttribute psTypeNameAttrib = attrib as PSTypeNameAttribute;
            if (psTypeNameAttrib != null)
            {
                result = string.Format(
                    CultureInfo.InvariantCulture,
                    PSTypeNameFormat, 
                    prefix, 
                    CodeGeneration.EscapeSingleQuotedStringContent(psTypeNameAttrib.PSTypeName));
                return result;
            }

            ObsoleteAttribute obsoleteAttrib = attrib as ObsoleteAttribute;
            if (obsoleteAttrib != null)
            {
                string parameters = string.Empty;
                if (obsoleteAttrib.IsError)
                {
                    string message = "'" + CodeGeneration.EscapeSingleQuotedStringContent(obsoleteAttrib.Message) + "'";
                    parameters = message + ", $true";
                }
                else if (obsoleteAttrib.Message != null)
                {
                    parameters = "'" + CodeGeneration.EscapeSingleQuotedStringContent(obsoleteAttrib.Message) + "'";
                }

                result = string.Format(
                    CultureInfo.InvariantCulture,
                    ObsoleteFormat,
                    prefix,
                    parameters);
                return result;
            }

            return null;
        }

        #endregion
    }

    /// <summary>
    /// The metadata associated with a bindable type
    /// </summary>
    /// 
    internal class InternalParameterMetadata
    {
        #region ctor

        /// <summary>
        /// Gets or constructs an instance of the InternalParameterMetadata for the specified runtime-defined parameters.
        /// </summary>
        /// 
        /// <param name="runtimeDefinedParameters">
        /// The runtime-defined parameter collection that describes the parameters and their metadata.
        /// </param>
        /// 
        /// <param name="processingDynamicParameters">
        /// True if dynamic parameters are being processed, or false otherwise.
        /// </param>
        /// 
        /// <param name="checkNames">
        /// Check for reserved parameter names.
        /// </param>
        /// 
        /// <returns>
        /// An instance of the TypeMetdata for the specified runtime-defined parameters. The metadata 
        /// is always constructed on demand and never cached.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="runtimeDefinedParameters"/> is null.
        /// </exception>
        /// 
        /// <exception cref="MetadataException">
        /// If a parameter defines the same parameter-set name multiple times.
        /// If the attributes could not be read from a property or field.
        /// </exception>
        /// 
        internal static InternalParameterMetadata Get(RuntimeDefinedParameterDictionary runtimeDefinedParameters,
                                                      bool processingDynamicParameters,
                                                      bool checkNames)
        {
            if (runtimeDefinedParameters == null)
            {
                throw PSTraceSource.NewArgumentNullException("runtimeDefinedParameter");
            }

            return new InternalParameterMetadata(runtimeDefinedParameters, processingDynamicParameters, checkNames);
        } 

        /// <summary>
        /// Gets or constructs an instance of the InternalParameterMetadata for the specified type.
        /// </summary>
        /// 
        /// <param name="type">
        /// The type to get the metadata for.
        /// </param>
        /// 
        /// <param name="context">
        /// The current engine context.
        /// </param>
        /// 
        /// <param name="processingDynamicParameters">
        /// True if dynamic parameters are being processed, or false otherwise.
        /// </param>
        /// 
        /// <returns>
        /// An instance of the TypeMetdata for the specified type. The metadata may get
        /// constructed on-demand or may be retrieved from the cache.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="type"/> is null.
        /// </exception>
        /// 
        /// <exception cref="MetadataException">
        /// If a parameter defines the same parameter-set name multiple times.
        /// If the attributes could not be read from a property or field.
        /// </exception>
        /// 
        internal static InternalParameterMetadata Get(Type type, ExecutionContext context, bool processingDynamicParameters)
        {
            if (type == null)
            {
                throw PSTraceSource.NewArgumentNullException("type");
            }

            InternalParameterMetadata result;
            if (context == null || !ParameterMetadataCache.TryGetValue(type.AssemblyQualifiedName, out result))
            {
                result = new InternalParameterMetadata(type, processingDynamicParameters);

                if (context != null)
                {
                    ParameterMetadataCache.TryAdd(type.AssemblyQualifiedName, result);
                }
            }
            return result;
        }  // GetMetadata

        // 
        /// <summary>
        /// Constructs an instance of the InternalParameterMetadata using the metadata in the 
        /// runtime-defined parameter collection.
        /// </summary>
        /// 
        /// <param name="runtimeDefinedParameters">
        /// The collection of runtime-defined parameters that declare the parameters and their
        /// metadata.
        /// </param>
        /// 
        /// <param name="processingDynamicParameters">
        /// True if dynamic parameters are being processed, or false otherwise.
        /// </param>
        /// 
        /// <param name="checkNames">
        /// Check if the parameter name has been reserved.
        /// </param>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="runtimeDefinedParameters"/> is null.
        /// </exception>
        /// 
        /// <exception cref="MetadataException">
        /// If a parameter defines the same parameter-set name multiple times.
        /// If the attributes could not be read from a property or field.
        /// </exception>
        /// 
        internal InternalParameterMetadata(RuntimeDefinedParameterDictionary runtimeDefinedParameters, bool processingDynamicParameters, bool checkNames)
        {
            if (runtimeDefinedParameters == null)
            {
                throw PSTraceSource.NewArgumentNullException("runtimeDefinedParameters");
            }
            ConstructCompiledParametersUsingRuntimeDefinedParameters(runtimeDefinedParameters, processingDynamicParameters, checkNames);
        }

        // 
        /// <summary>
        /// Constructs an instance of the InternalParameterMetadata using the reflection information retrieved
        /// from the enclosing bindable object type.
        /// </summary>
        /// 
        /// <param name="type">
        /// The type information for the bindable object
        /// </param>
        /// 
        /// <param name="processingDynamicParameters">
        /// True if dynamic parameters are being processed, or false otherwise.
        /// </param>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="type"/> is null.
        /// </exception>
        /// 
        /// <exception cref="MetadataException">
        /// If a parameter defines the same parameter-set name multiple times.
        /// If the attributes could not be read from a property or field.
        /// </exception>
        /// 
        internal InternalParameterMetadata(Type type, bool processingDynamicParameters)
        {
            if (type == null)
            {
                throw PSTraceSource.NewArgumentNullException("type");
            }

            this.type = type;
            this.typeName = type.Name;

            ConstructCompiledParametersUsingReflection(processingDynamicParameters);
        }

        #endregion ctor

        /// <summary>
        /// Gets the type name of the bindable type
        /// </summary>
        /// 
        internal string TypeName
        {
            get
            {
                return typeName;
            }
        }
        private string typeName = String.Empty;

        /// <summary>
        /// Gets a dictionary of the compiled parameter metadata for this Type. 
        /// The dictionary keys are the names of the parameters (or aliases) and
        /// the values are the compiled parameter metdata.
        /// </summary>
        /// 
        internal Dictionary<string, CompiledCommandParameter> BindableParameters
        {
            get
            {
                return bindableParameters;
            }
        }
        private Dictionary<string, CompiledCommandParameter> bindableParameters =
            new Dictionary<string, CompiledCommandParameter>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets a dictionary of the parameters that have been aliased to other names. The key is
        /// the alias name and the value is the CompiledCommandParameter metadata.
        /// </summary>
        /// 
        internal Dictionary<string, CompiledCommandParameter> AliasedParameters
        {
            get
            {
                return aliasedParameters;
            }
        }
        private Dictionary<string, CompiledCommandParameter> aliasedParameters =
            new Dictionary<string, CompiledCommandParameter>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The type information for the class that implements the bindable object.
        /// This member is null in all cases except when constructed with using reflection
        /// against the Type.
        /// </summary>
        private Type type;

        /// <summary>
        /// The flags used when reflecting against the object to create the metadata
        /// </summary>
        internal static readonly BindingFlags metaDataBindingFlags = (BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        #region helper methods

        /// <summary>
        /// Fills in the data for an instance of this class using the specified runtime-defined parameters
        /// </summary>
        /// 
        /// <param name="runtimeDefinedParameters">
        /// A description of the parameters and their metadata.
        /// </param>
        /// 
        /// <param name="processingDynamicParameters">
        /// True if dynamic parameters are being processed, or false otherwise.
        /// </param>
        /// 
        /// <param name="checkNames">
        /// Check if the parameter name has been reserved.
        /// </param>
        /// 
        /// <exception cref="MetadataException">
        /// If a parameter defines the same parameter-set name multiple times.
        /// If the attributes could not be read from a property or field.
        /// </exception>
        /// 
        private void ConstructCompiledParametersUsingRuntimeDefinedParameters(
            RuntimeDefinedParameterDictionary runtimeDefinedParameters,
            bool processingDynamicParameters,
            bool checkNames)
        {
            Diagnostics.Assert (
                runtimeDefinedParameters != null,
                "This method should only be called when constructed with a valid runtime-defined parameter collection");

            foreach (RuntimeDefinedParameter parameterDefinition in runtimeDefinedParameters.Values)
            {
                // Create the compiled parameter and add it to the bindable parameters collection

                // NTRAID#Windows Out Of Band Releases-926374-2005/12/22-JonN
                if (null == parameterDefinition)
                    continue;

                CompiledCommandParameter parameter = new CompiledCommandParameter(parameterDefinition, processingDynamicParameters);
                AddParameter(parameter, checkNames);
            }
        } // ConstructCompiledParametersUsingRuntimeDefinedParameters


        /// <summary>
        /// Compiles the parameter using reflection against the CLR type.
        /// </summary>
        /// 
        /// <param name="processingDynamicParameters">
        /// True if dynamic parameters are being processed, or false otherwise.
        /// </param>
        /// 
        /// <exception cref="MetadataException">
        /// If a parameter defines the same parameter-set name multiple times.
        /// If the attributes could not be read from a property or field.
        /// </exception>
        /// 
        private void ConstructCompiledParametersUsingReflection(bool processingDynamicParameters)
        {
            Diagnostics.Assert(
                type != null,
                "This method should only be called when constructed with the Type");

            // Get the property and field info

            PropertyInfo[] properties = type.GetProperties(metaDataBindingFlags);
            FieldInfo[] fields = type.GetFields(metaDataBindingFlags);

            foreach(PropertyInfo property in properties)
            {
                // Check whether the property is a parameter
                if (!IsMemberAParameter(property))
                {
                    continue;
                }

                AddParameter(property, processingDynamicParameters);
            }

            foreach(FieldInfo field in fields)
            {
                // Check whether the field is a parameter
                if (!IsMemberAParameter(field))
                {
                    continue;
                }

                AddParameter(field, processingDynamicParameters);
            }
        } // ConstructCompiledParametersUsingReflection

        private void CheckForReservedParameter(string name)
        {
            if (name.Equals("SelectProperty", StringComparison.OrdinalIgnoreCase)
                ||
                name.Equals("SelectObject", StringComparison.OrdinalIgnoreCase))
            {
                throw new MetadataException(
                            "ReservedParameterName",
                            null,
                            DiscoveryExceptions.ReservedParameterName,
                            name);
            }
        }

        // NTRAID#Windows Out Of Band Releases-906345-2005/06/30-JeffJon
        // This call verifies that the parameter is unique or 
        // can be deemed unique. If not, an exception is thrown.
        // If it is unique (or deemed unique), then it is added
        // to the bindableParameters collection
        //
        private void AddParameter(MemberInfo member, bool processingDynamicParameters)
        {
            bool error = false;
            bool useExisting = false;

            CheckForReservedParameter(member.Name);

            do // false loop
            {
                CompiledCommandParameter existingParameter;
                if (!bindableParameters.TryGetValue(member.Name, out existingParameter))
                {
                    break;
                }

                Type existingParamDeclaringType = existingParameter.DeclaringType;

                if (existingParamDeclaringType == null)
                {
                    error = true;
                    break;
                }

                if (existingParamDeclaringType.IsSubclassOf(member.DeclaringType))
                {
                    useExisting = true;
                    break;
                }

                if (member.DeclaringType.IsSubclassOf(existingParamDeclaringType))
                {
                    // Need to swap out the new member for the parameter definition
                    // that is already defined.

                    RemoveParameter(existingParameter);
                    break;
                }

                error = true;
            } while (false);

            if (error)
            {
                // A duplicate parameter was found and could not be deemed unique 
                // through inheritance. 

                throw new MetadataException(
                    "DuplicateParameterDefinition",
                    null,
                    ParameterBinderStrings.DuplicateParameterDefinition,
                    member.Name);
            }

            if (!useExisting)
            {
                CompiledCommandParameter parameter = new CompiledCommandParameter(member, processingDynamicParameters);
                AddParameter(parameter, true);
            }
        }

        private void AddParameter(CompiledCommandParameter parameter, bool checkNames)
        {
            if (checkNames)
            {
                CheckForReservedParameter(parameter.Name);
            }

            bindableParameters.Add(parameter.Name, parameter);

            // Now add entries in the parameter aliases collection for any aliases.

            foreach (string alias in parameter.Aliases)
            {
                // NTRAID#Windows Out Of Band Releases-917356-JonN
                if (aliasedParameters.ContainsKey(alias))
                {
                    throw new MetadataException(
                            "AliasDeclaredMultipleTimes",
                            null,
                            DiscoveryExceptions.AliasDeclaredMultipleTimes,
                            alias);
                }
                aliasedParameters.Add(alias, parameter);
            }
        }

        private void RemoveParameter(CompiledCommandParameter parameter)
        {
            bindableParameters.Remove(parameter.Name);

            // Now add entries in the parameter aliases collection for any aliases.

            foreach (string alias in parameter.Aliases)
            {
                aliasedParameters.Remove(alias);
            }
        }

        /// <summary>
        /// Determines if the specified member represents a parameter based on its attributes
        /// </summary>
        /// 
        /// <param name="member">
        /// The member to check to see if it is a parameter.
        /// </param>
        /// 
        /// <returns>
        /// True if at least one ParameterAttribute is declared on the member, or false otherwise.
        /// </returns>
        /// 
        /// <exception cref="MetadataException">
        /// If GetCustomAttributes fails on <paramref name="member"/>.
        /// </exception>
        /// 
        private static bool IsMemberAParameter(MemberInfo member)
        {
            bool result = false;

            try
            {
                // MemberInfo.GetCustomAttributes returns IEnumerable<Attribute> in CoreCLR
                var attributes = member.GetCustomAttributes(typeof(ParameterAttribute), false);
                if (attributes.Any())
                {
                    result = true;
                }
            }
            catch (MetadataException metadataException)
            {
                throw new MetadataException(
                    "GetCustomAttributesMetadataException", 
                    metadataException, 
                    Metadata.MetadataMemberInitialization,
                    member.Name, 
                    metadataException.Message);
            }
            catch (ArgumentException argumentException)
            {
                throw new MetadataException(
                    "GetCustomAttributesArgumentException",
                    argumentException, 
                    Metadata.MetadataMemberInitialization,
                    member.Name, 
                    argumentException.Message);
            }

            return result;
        } // IsMemberAParameter

        #endregion helper methods

        #region Metadata cache

        /// <summary>
        /// The cache of the type metadata. The key for the cache is the Type.FullName.
        /// Note, this is a case-sensitive dictionary because Type names are case sensitive.
        /// </summary>
        private static System.Collections.Concurrent.ConcurrentDictionary<string, InternalParameterMetadata> ParameterMetadataCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, InternalParameterMetadata>(StringComparer.Ordinal);

        #endregion Metadata cache
    } // CompiledCommandParameter
}

