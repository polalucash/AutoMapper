using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AutoMapper.Configuration;
using AutoMapper.Execution;

namespace AutoMapper
{
    using AutoMapper.Features;
    using Internal;
    using System.ComponentModel;
    using static Expression;

    /// <summary>
    /// Main configuration object holding all mapping configuration for a source and destination type
    /// </summary>
    [DebuggerDisplay("{SourceType.Name} -> {DestinationType.Name}")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class TypeMap
    {
        private readonly HashSet<LambdaExpression> _afterMapActions = new HashSet<LambdaExpression>();
        private readonly HashSet<LambdaExpression> _beforeMapActions = new HashSet<LambdaExpression>();
        private readonly HashSet<TypePair> _includedDerivedTypes = new HashSet<TypePair>();
        private readonly HashSet<TypePair> _includedBaseTypes = new HashSet<TypePair>();
        private readonly Dictionary<string, PropertyMap> _propertyMaps = new Dictionary<string, PropertyMap>();
        private readonly Dictionary<MemberPath, PathMap> _pathMaps = new Dictionary<MemberPath, PathMap>();
        private readonly Dictionary<MemberInfo, SourceMemberConfig> _sourceMemberConfigs = new Dictionary<MemberInfo, SourceMemberConfig>();
        private PropertyMap[] _orderedPropertyMaps;
        private bool _sealed;
        private readonly HashSet<TypeMap> _inheritedTypeMaps = new HashSet<TypeMap>();
        private readonly HashSet<IncludedMember> _includedMembersTypeMaps = new HashSet<IncludedMember>();
        private readonly List<ValueTransformerConfiguration> _valueTransformerConfigs = new List<ValueTransformerConfiguration>();

        public TypeMap(Type sourceType, Type destinationType, ProfileMap profile)
        {
            Types = new TypePair(sourceType, destinationType);
            Profile = profile;
        }

        public PathMap FindOrCreatePathMapFor(LambdaExpression destinationExpression, MemberPath path, TypeMap typeMap)
        {
            var pathMap = _pathMaps.GetOrDefault(path);
            if(pathMap == null)
            {
                pathMap = new PathMap(destinationExpression, path, typeMap);
                AddPathMap(pathMap);
            }
            return pathMap;
        }

        private void AddPathMap(PathMap pathMap) => _pathMaps.Add(pathMap.MemberPath, pathMap);

        public Features<IRuntimeFeature> Features { get; } = new Features<IRuntimeFeature>();

        public LambdaExpression MapExpression { get; private set; }

        public TypePair Types { get; }

        public ConstructorMap ConstructorMap { get; set; }

        public TypeDetails SourceTypeDetails => Profile.GetTypeDetails(SourceType);
        private TypeDetails DestinationTypeDetails => Profile.GetTypeDetails(DestinationType);

        public Type SourceType => Types.SourceType;
        public Type DestinationType => Types.DestinationType;

        public ProfileMap Profile { get; }

        public LambdaExpression CustomMapFunction { get; set; }
        public LambdaExpression CustomMapExpression { get; set; }
        public LambdaExpression CustomCtorFunction { get; set; }
        public LambdaExpression CustomCtorExpression { get; set; }

        public Type DestinationTypeOverride { get; set; }
        public Type DestinationTypeToUse => DestinationTypeOverride ?? DestinationType;

        public bool ConstructDestinationUsingServiceLocator { get; set; }

        public bool IncludeAllDerivedTypes { get; set; }

        public MemberList ConfiguredMemberList { get; set; }

        public IReadOnlyCollection<TypePair> IncludedDerivedTypes => _includedDerivedTypes;
        public IReadOnlyCollection<TypePair> IncludedBaseTypes => _includedBaseTypes;

        public IEnumerable<LambdaExpression> BeforeMapActions => _beforeMapActions;
        public IEnumerable<LambdaExpression> AfterMapActions => _afterMapActions;
        public IReadOnlyCollection<ValueTransformerConfiguration> ValueTransformers => _valueTransformerConfigs;

        public bool PreserveReferences { get; set; }
        public LambdaExpression Condition { get; set; }

        public int MaxDepth { get; set; }

        public Type TypeConverterType { get; set; }
        public bool DisableConstructorValidation { get; set; }

        public IEnumerable<PropertyMap> PropertyMaps => _orderedPropertyMaps ?? (IEnumerable<PropertyMap>)_propertyMaps.Values;
        public IEnumerable<PathMap> PathMaps => _pathMaps.Values;
        public IEnumerable<IMemberMap> MemberMaps => PropertyMaps.Cast<IMemberMap>().Concat(PathMaps).Concat(GetConstructorMemberMaps());

        public bool? IsValid { get; set; }
        internal bool WasInlineChecked { get; set; }

        public bool PassesCtorValidation =>
            DisableConstructorValidation
            || CustomCtorExpression != null
            || CustomCtorFunction != null
            || ConstructDestinationUsingServiceLocator
            || ConstructorMap?.CanResolve == true
            || DestinationTypeToUse.IsInterface
            || DestinationTypeToUse.IsAbstract
            || DestinationTypeToUse.IsGenericTypeDefinition
            || DestinationTypeToUse.IsValueType
            || TypeDetails.GetConstructors(DestinationType, Profile).Any(c => c.AllParametersOptional());

        public MemberInfo[] DestinationSetters => DestinationTypeDetails.WriteAccessors;
        public ConstructorParameters[] DestinationConstructors => DestinationTypeDetails.Constructors;

        public bool IsConstructorMapping =>
            CustomCtorExpression == null
            && CustomCtorFunction == null
            && !ConstructDestinationUsingServiceLocator
            && (ConstructorMap?.CanResolve ?? false);

        public bool HasTypeConverter =>
            CustomMapFunction != null
            || CustomMapExpression != null
            || TypeConverterType != null;

        public bool ShouldCheckForValid =>
            !HasTypeConverter
            && DestinationTypeOverride == null
            && ConfiguredMemberList != MemberList.None
            && !(IsValid ?? false);

        public LambdaExpression[] IncludedMembers { get; internal set; } = Array.Empty<LambdaExpression>();
        public string[] IncludedMembersNames { get; internal set; } = Array.Empty<string>();

        public IReadOnlyCollection<IncludedMember> IncludedMembersTypeMaps => _includedMembersTypeMaps;

        public Type MakeGenericType(Type type) => type.IsGenericTypeDefinition ?
            type.MakeGenericType(SourceType.GenericTypeArguments.Concat(DestinationType.GenericTypeArguments).Take(type.GetGenericParameters().Length).ToArray()) :
            type;

        public IEnumerable<LambdaExpression> GetAllIncludedMembers() => IncludedMembers.Concat(GetUntypedIncludedMembers());

        private IEnumerable<LambdaExpression> GetUntypedIncludedMembers() =>
            SourceType.IsGenericTypeDefinition ?
                Array.Empty<LambdaExpression>() :
                IncludedMembersNames.Select(name => ExpressionFactory.MemberAccessLambda(SourceType, name));

        public bool ConstructorParameterMatches(string destinationPropertyName) =>
            ConstructorMap.CtorParams.Any(c => string.Equals(c.Parameter.Name, destinationPropertyName, StringComparison.OrdinalIgnoreCase));

        public void AddPropertyMap(MemberInfo destProperty, IEnumerable<MemberInfo> resolvers)
        {
            var propertyMap = new PropertyMap(destProperty, this);

            propertyMap.ChainMembers(resolvers);

            AddPropertyMap(propertyMap);
        }

        private void AddPropertyMap(PropertyMap propertyMap) => _propertyMaps.Add(propertyMap.DestinationName, propertyMap);

        public string[] GetUnmappedPropertyNames()
        {
            var autoMappedProperties = GetPropertyNames(PropertyMaps);

            IEnumerable<string> properties;

            if(ConfiguredMemberList == MemberList.Destination)
            {
                properties = Profile.CreateTypeDetails(DestinationType).WriteAccessors
                    .Select(p => p.Name)
                    .Except(autoMappedProperties)
                    .Except(PathMaps.Select(p => p.MemberPath.First.Name));
                if (IsConstructorMapping)
                {
                    properties = properties.Where(p => !ConstructorParameterMatches(p));
                }
            }
            else
            {
               var redirectedSourceMembers = MemberMaps
                    .Where(pm => pm.IsMapped && pm.SourceMember != null && pm.SourceMember.Name != pm.DestinationName)
                    .Select(pm => pm.SourceMember.Name);

               var ignoredSourceMembers = _sourceMemberConfigs.Values
                   .Where(smc => smc.IsIgnored())
                   .Select(pm => pm.SourceMember.Name);

                properties = Profile.CreateTypeDetails(SourceType).ReadAccessors
                    .Select(p => p.Name)
                    .Except(autoMappedProperties)
                    .Except(redirectedSourceMembers)
                    .Except(ignoredSourceMembers);
            }

            return properties.Where(memberName => !Profile.GlobalIgnores.Any(memberName.StartsWith)).ToArray();
            string GetPropertyName(PropertyMap pm) => ConfiguredMemberList == MemberList.Destination
                ? pm.DestinationName
                : pm.SourceMembers.Count > 1
                    ? pm.SourceMembers.First().Name 
                    : pm.SourceMember?.Name ?? pm.DestinationName;
            string[] GetPropertyNames(IEnumerable<PropertyMap> propertyMaps) => propertyMaps.Where(pm => pm.IsMapped).Select(GetPropertyName).ToArray();
        }

        public PropertyMap FindOrCreatePropertyMapFor(MemberInfo destinationProperty)
        {
            var propertyMap = GetPropertyMap(destinationProperty.Name);

            if (propertyMap != null) return propertyMap;

            propertyMap = new PropertyMap(destinationProperty, this);

            AddPropertyMap(propertyMap);

            return propertyMap;
        }

        public void IncludeDerivedTypes(in TypePair derivedTypes)
        {
            CheckDifferent(derivedTypes);
            _includedDerivedTypes.Add(derivedTypes);
        }

        private void CheckDifferent(in TypePair types)
        {
            if (types == Types)
            {
                throw new InvalidOperationException("You cannot include a type map into itself.");
            }
        }

        public void IncludeBaseTypes(in TypePair baseTypes)
        {
            CheckDifferent(baseTypes);
            _includedBaseTypes.Add(baseTypes);
        }

        internal void IgnorePaths(MemberInfo destinationMember)
        {
            foreach(var pathMap in PathMaps.Where(pm => pm.MemberPath.First == destinationMember))
            {
                pathMap.Ignored = true;
            }
        }

        public Type GetDerivedTypeFor(Type derivedSourceType)
        {
            if (DestinationTypeOverride != null)
            {
                return DestinationTypeOverride;
            }
            // This might need to be fixed for multiple derived source types to different dest types
            var match = _includedDerivedTypes.FirstOrDefault(tp => tp.SourceType == derivedSourceType);

            return match.DestinationType ?? DestinationType;
        }

        public bool HasDerivedTypesToInclude => _includedDerivedTypes.Count > 0 || DestinationTypeOverride != null;

        public void AddBeforeMapAction(LambdaExpression beforeMap) => _beforeMapActions.Add(beforeMap);

        public void AddAfterMapAction(LambdaExpression afterMap) => _afterMapActions.Add(afterMap);

        public void AddValueTransformation(ValueTransformerConfiguration valueTransformerConfiguration) => _valueTransformerConfigs.Add(valueTransformerConfiguration);

        public void Seal(IGlobalConfiguration configurationProvider)
        {
            if(_sealed)
            {
                return;
            }
            _sealed = true;
            foreach (var inheritedTypeMap in _inheritedTypeMaps)
            {
                _includedMembersTypeMaps.UnionWith(inheritedTypeMap._includedMembersTypeMaps);
            }
            foreach (var includedMemberTypeMap in _includedMembersTypeMaps)
            {
                includedMemberTypeMap.TypeMap.Seal(configurationProvider);
                ApplyIncludedMemberTypeMap(includedMemberTypeMap);
            }
            foreach (var inheritedTypeMap in _inheritedTypeMaps)
            {
                ApplyInheritedTypeMap(inheritedTypeMap);
            }
            _orderedPropertyMaps = PropertyMaps.OrderBy(map => map.MappingOrder).ToArray();
            _propertyMaps.Clear();

            MapExpression = CreateMapperLambda(configurationProvider, null);

            Features.Seal(configurationProvider);
        }

        internal LambdaExpression CreateMapperLambda(IGlobalConfiguration configurationProvider, HashSet<TypeMap> typeMapsPath) =>
            Types.IsGenericTypeDefinition ? null : new TypeMapPlanBuilder(configurationProvider, this).CreateMapperLambda(typeMapsPath);

        private PropertyMap GetPropertyMap(string name) => _propertyMaps.GetOrDefault(name);

        private PropertyMap GetPropertyMap(PropertyMap propertyMap) => GetPropertyMap(propertyMap.DestinationName);

        public bool AddMemberMap(IncludedMember includedMember) => _includedMembersTypeMaps.Add(includedMember);

        public SourceMemberConfig FindOrCreateSourceMemberConfigFor(MemberInfo sourceMember)
        {
            var config = _sourceMemberConfigs.GetOrDefault(sourceMember);

            if(config != null) return config;

            config = new SourceMemberConfig(sourceMember);
            AddSourceMemberConfig(config);
            return config;
        }

        private void AddSourceMemberConfig(SourceMemberConfig config) => _sourceMemberConfigs.Add(config.SourceMember, config);

        public bool AddInheritedMap(TypeMap inheritedTypeMap) => _inheritedTypeMaps.Add(inheritedTypeMap);

        private void ApplyIncludedMemberTypeMap(IncludedMember includedMember)
        {
            var typeMap = includedMember.TypeMap;
            var includedMemberMaps = typeMap.PropertyMaps.
                Where(m => m.CanResolveValue && GetPropertyMap(m)==null)
                .Select(p => new PropertyMap(p, this, includedMember))
                .ToArray();
            var notOverridenPathMaps = NotOverridenPathMaps(typeMap);
            if(includedMemberMaps.Length == 0 && notOverridenPathMaps.Length == 0)
            {
                return;
            }
            foreach(var includedMemberMap in includedMemberMaps)
            {
                AddPropertyMap(includedMemberMap);
                foreach(var transformer in typeMap.ValueTransformers)
                {
                    includedMemberMap.AddValueTransformation(transformer);
                }
            }
            _beforeMapActions.UnionWith(typeMap._beforeMapActions.Select(includedMember.Chain));
            _afterMapActions.UnionWith(typeMap._afterMapActions.Select(includedMember.Chain));
            foreach (var notOverridenPathMap in notOverridenPathMaps)
            {
                AddPathMap(new PathMap(notOverridenPathMap, this, includedMember) { CustomMapExpression = notOverridenPathMap.CustomMapExpression });
            }
        }

        private void ApplyInheritedTypeMap(TypeMap inheritedTypeMap)
        {
            foreach(var inheritedMappedProperty in inheritedTypeMap.PropertyMaps.Where(m => m.IsMapped))
            {
                var conventionPropertyMap = GetPropertyMap(inheritedMappedProperty);

                if(conventionPropertyMap != null)
                {
                    conventionPropertyMap.ApplyInheritedPropertyMap(inheritedMappedProperty);
                }
                else
                {
                    AddPropertyMap(new PropertyMap(inheritedMappedProperty, this));
                }
            }
            _beforeMapActions.UnionWith(inheritedTypeMap._beforeMapActions);
            _afterMapActions.UnionWith(inheritedTypeMap._afterMapActions);
            foreach (var inheritedSourceConfig in inheritedTypeMap._sourceMemberConfigs.Values)
            {
                if (!_sourceMemberConfigs.ContainsKey(inheritedSourceConfig.SourceMember))
                {
                    AddSourceMemberConfig(inheritedSourceConfig);
                }
            }
            var notOverridenPathMaps = NotOverridenPathMaps(inheritedTypeMap);
            foreach (var notOverridenPathMap in notOverridenPathMaps)
            {
                AddPathMap(notOverridenPathMap);
            }
            _valueTransformerConfigs.InsertRange(0, inheritedTypeMap._valueTransformerConfigs);
        }

        private PathMap[] NotOverridenPathMaps(TypeMap inheritedTypeMap) =>
            inheritedTypeMap.PathMaps.Where(baseConfig => !_pathMaps.ContainsKey(baseConfig.MemberPath)).ToArray();

        internal void CopyInheritedMapsTo(TypeMap typeMap) => typeMap._inheritedTypeMaps.UnionWith(_inheritedTypeMaps);

        private IEnumerable<IMemberMap> GetConstructorMemberMaps() => IsConstructorMapping ? ConstructorMap.CtorParams : Enumerable.Empty<IMemberMap>();
    }
}