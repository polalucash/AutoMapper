using AutoMapper.Mappers;

namespace AutoMapper.Internal
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    public class ProfileConfiguration : IProfileConfiguration
    {
        private readonly List<MethodInfo> _sourceExtensionMethods = new List<MethodInfo>();

        public ProfileConfiguration()
        {
            MemberConfigurations.Add(new MemberConfiguration());
            ConstructorMappingEnabled = true;
            IncludeSourceExtensionMethods(typeof(Enumerable).Assembly());
            ShouldMapProperty = p => p.IsPublic();
            ShouldMapField = f => f.IsPublic;
        }

        public IList<IMemberConfiguration> MemberConfigurations { get; } = new List<IMemberConfiguration>();
        public IList<IConditionalObjectMapper> TypeConfigurations { get; } = new List<IConditionalObjectMapper>();
        public IConditionalObjectMapper AddConditionalObjectMapper(string profile = ConfigurationStore.DefaultProfileName)
        {
            var condition = new ConditionalObjectMapper(profile);
            TypeConfigurations.Add(condition);
            return condition;
        }

        public bool ConstructorMappingEnabled { get; set; }
        public bool DataReaderMapperYieldReturnEnabled { get; set; }
        public IEnumerable<MethodInfo> SourceExtensionMethods => _sourceExtensionMethods;

        public Func<PropertyInfo, bool> ShouldMapProperty { get; set; }

        public Func<FieldInfo, bool> ShouldMapField { get; set; }

        public void IncludeSourceExtensionMethods(Assembly assembly)
        {
            //http://stackoverflow.com/questions/299515/c-sharp-reflection-to-identify-extension-methods
            _sourceExtensionMethods.AddRange(assembly.GetTypes()
                .Where(type => type.IsSealed() && !type.IsGenericType() && !type.IsNested)
                .SelectMany(type => type.GetDeclaredMethods().Where(mi => mi.IsStatic))
                .Where(method => method.IsDefined(typeof(ExtensionAttribute), false))
                .Where(method => method.GetParameters().Length == 1));
        }
    }
}