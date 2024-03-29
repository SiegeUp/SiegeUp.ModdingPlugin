﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace SiegeUp.ModdingPlugin.DevUtils
{
	public class ClassesParser
	{
		public Type[] AllowedCustomAttributes { get; private set; } = { typeof(SerializeField) };

		public ClassesParser SetAllowedCustomAttributes(params Type[] attributes)
		{
			AllowedCustomAttributes = attributes?.Append(typeof(SerializeField)).Distinct().ToArray() ?? Array.Empty<Type>();
            return this;
		}

		public IReadOnlyCollection<ClassInfo> ParseTypesWithAttributes(Type[] attributeType)
        {
            var allTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(i => i.GetTypes());
            var types = allTypes.Where(x => attributeType.Any(y => x.IsDefined(y, false))).ToArray();
			return ParseTypes(types);
		}

		public IReadOnlyCollection<ClassInfo> ParseTypes(Type[] types)
		{

            var classesInfo = GetClassesInfo(types);
            return classesInfo;
        }

        HashSet<ClassInfo> GetClassesInfo(Type[] types)
		{
			var result = new HashSet<ClassInfo>();
			var knownTypes = new HashSet<Type>(types);
			var stack = new Stack<Type>(types);
			while (stack.Count > 0)
			{
				var type = stack.Pop();
                if (type.CustomAttributes.Any(i => i.AttributeType == typeof(DisableReference)))
                    continue;
				if (IsSystemType(type) || !IsSimpleType(type))
					continue;
				var classInfo = new ClassInfo(type, AllowedCustomAttributes, types.Contains(type));
				foreach (var a in classInfo.Dependencies.Except(knownTypes))
					stack.Push(a);
				knownTypes.UnionWith(classInfo.Dependencies);
				result.Add(classInfo);
			}
			return result;
		}

        public static void SerializeClass(string outputFolder, ClassInfo classInfo)
		{
            using (var sw = new StreamWriter(Path.Combine(outputFolder, classInfo.Type + ".cs")))
            {
                SerializeClassUsings(sw, classInfo.Usings);
				sw.WriteLine();
                SerializeClassInfo(sw, classInfo);
            }
        }

        public static void SerializeClassInfo(StreamWriter output, ClassInfo classInfo, string indent = "")
		{
            SerializeAttributes(output, classInfo.CustomAttributesData, indent);
			if (classInfo.IsSerializable)
			{
				SerializeAttribute(output, typeof(SerializableAttribute), null, indent);
			}
			SerializeTypeDeclaration(output, classInfo, indent);
			output.WriteLine(indent + "{");
			if (classInfo.Type.IsEnum)
			{
				SerializeEnumBody(output, classInfo, indent + "\t");
			}
			else if (!classInfo.Type.IsInterface)
			{
				SerializeFields(output, classInfo.Fields, indent + "\t");
				foreach (var nestedType in classInfo.NestedTypes)
				{
					output.WriteLine();
					SerializeClassInfo(output, nestedType, indent + "\t");
				}
			}
			output.Write(indent);
			output.Write("}");
			output.WriteLine();
		}

        static void SerializeEnumBody(StreamWriter output, ClassInfo classInfo, string indent)
		{
			foreach (string value in classInfo.Type.GetEnumNames())
			{
				output.Write(indent);
				output.Write(value);
				output.WriteLine(",");
			}
		}

        static void SerializeTypeDeclaration(StreamWriter output, ClassInfo classInfo, string indent)
		{
			output.Write(indent);
			output.Write(SerializeModifiersAndKeyWords(classInfo.Type.GetTypeInfo()));
			if (classInfo.Type.IsEnum)
				output.Write(" enum ");
			else if (classInfo.Type.IsInterface)
				output.Write(" interface ");
			else if (classInfo.Type.IsValueType)
				output.Write(" struct ");
			else
				output.Write(classInfo.ShouldBePartial ? " partial class " : " class ");
			output.Write(SerializeMemberType(classInfo.Type, true));
			if (IsRequiredBaseType(classInfo.Type.BaseType))
				output.Write($" : {classInfo.Type.BaseType}");
			output.WriteLine();
		}

        static void SerializeFields(StreamWriter output, IEnumerable<ClassInfo.ClassFieldInfo> members, string indent = "\t")
		{
			foreach (var member in members) 
                SerializeField(output, member, indent);
		}

        static void SerializeField(StreamWriter output, ClassInfo.ClassFieldInfo member, string indent = "\t")
		{
			SerializeAttributes(output, member.CustomAttributesData, indent);
			output.Write(indent);
			string modifiers = SerializeModifiersAndKeyWords(member);
			if (modifiers != "")
			{
				output.Write(modifiers);
				output.Write(" ");
			}
			output.Write(SerializeMemberType(member.Type));
			output.Write(" ");
			output.Write(member.Name);
			output.WriteLine(";");
		}

        static string SerializeMemberType(Type type, bool usePlainName = false)
		{
			if (type.IsArray)
			{
				var subtype = type.GetElementType();
				string data = SerializeMemberType(subtype);
				string rankData = $"[{new string(',', type.GetArrayRank() - 1)}]";
				return subtype.IsArray ? data.Insert(data.IndexOf('['), rankData) : data + rankData;
			}
			else if (type.IsGenericType)
			{
				return $"{type.Name.Substring(0, type.Name.Length - 2)}<{string.Join(", ", type.GetGenericArguments().Select(x => SerializeMemberType(x)))}>";
			}
			else
			{
				if (usePlainName)
					return type.Name;
				var typeParts = new[]
				{
					type.Namespace ?? "",
					type.DeclaringType?.Name ?? "",
					type.Name
				}.Where(x => x != "");
				return string.Join(".", typeParts);
			}
		}

        static void SerializeAttributes(StreamWriter output, IEnumerable<CustomAttributeData> attributes, string indent = "")
		{
			foreach (var attr in attributes)
				SerializeAttribute(output, attr.AttributeType, attr.ConstructorArguments, indent);
		}

        static void SerializeAttribute(StreamWriter output, Type attribute, IList<CustomAttributeTypedArgument> args, string indent)
		{
			output.Write($"{indent}[{attribute}");
			if (args != null && args.Count > 0)
				output.Write($"({string.Join(", ", GetAttributeArgsStringValues(args))})");
			output.WriteLine("]");
		}

        static IEnumerable<string> GetAttributeArgsStringValues(IEnumerable<CustomAttributeTypedArgument> args)
		{
			foreach (var arg in args)
			{
				if (arg.Value == null)
					yield return "null";
				else if (arg.ArgumentType == typeof(Type) || arg.ArgumentType == typeof(string))
					yield return arg.ToString();
				else
					yield return arg.Value?.ToString();
			}
		}

        public static void SerializeClassUsings(StreamWriter output, IEnumerable<string> namespaces)
		{
			foreach (string ns in namespaces)
				output.WriteLine($"using {ns};");
		}

        static string SerializeModifiersAndKeyWords(MemberInfo member)
		{
			bool isPublic, isStatic, isVirtual, isAbstract;
			if (member is FieldInfo field)
			{
				(isPublic, isStatic, isVirtual, isAbstract) = (field.IsPublic, field.IsStatic, false, false);
			}
			else if (member is PropertyInfo property)
			{
				var getMethod = property.GetGetMethod(true);
				(isPublic, isStatic, isVirtual, isAbstract) =
					(property.GetGetMethod()?.IsPublic ?? false, getMethod.IsStatic, getMethod.IsVirtual, getMethod.IsAbstract);
			}
			else if (member is MethodInfo method)
			{
				(isPublic, isStatic, isVirtual, isAbstract) =
					(method.IsPublic, method.IsStatic, method.IsVirtual, method.IsAbstract);
			}
			else if (member is TypeInfo type)
			{
				(isPublic, isStatic, isVirtual, isAbstract) =
					(!type.IsNotPublic, type.IsAbstract && type.IsSealed, false, type.IsAbstract && !type.IsSealed);
			}
            else
            {
                throw new NotImplementedException("Not supported member type");
            }

            return string.Join(
				" ",
				new[] { isPublic ? "public" : "", isStatic ? "static" : "", isVirtual ? "virtual" : "", isAbstract ? "abstract" : "" }
					.Where(x => x != ""));
		}

        static bool IsRequiredBaseType(Type type)
		{
			return type != typeof(object) && type != typeof(ValueType) && type != typeof(Enum);
		}

        static bool IsSimpleType(Type type)
		{
			return !(type.IsArray || type.IsGenericType);
		}

        static bool IsSystemType(Type type)
		{
			string assembly = type.Assembly.GetName().Name;
			return assembly == "mscorlib" || assembly.StartsWith("Unity") || assembly.StartsWith("System");
		}

        public class ClassInfo
		{
            public string[] Usings { get; }
            public Type Type { get; }
            public Type[] Dependencies { get; }
            public bool ShouldBePartial { get; }
            public bool IsSerializable { get; }
            public CustomAttributeData[] CustomAttributesData { get; }
            public ClassFieldInfo[] Fields { get; }
            public ClassInfo[] NestedTypes { get; }

            static readonly BindingFlags CommonBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

			public ClassInfo(Type type, Type[] allowedCustomAttributes, bool shouldBePartial)
            {
                ShouldBePartial = shouldBePartial;
				Type = type;
				Fields = GetTypeOwnFields(type, allowedCustomAttributes);
				var fieldsDirectDeps = GetVisibleDependencies(Fields.Select(x => x.Type)).ToArray();
				NestedTypes = GetRequiredNestedTypes(type, allowedCustomAttributes, fieldsDirectDeps);
				IsSerializable = (type.Attributes & TypeAttributes.Serializable) == TypeAttributes.Serializable;
				CustomAttributesData = GetCustomAttributesData(type, allowedCustomAttributes);
				var deps = new List<Type>();
				deps.AddRange(NestedTypes.SelectMany(x => x.Dependencies));
				deps.Add(type.BaseType);
				deps.AddRange(Fields.Select(x => x.Type));
				deps.AddRange(CustomAttributesData.Select(x => x.AttributeType));
				deps.AddRange(Fields.SelectMany(x => x.CustomAttributesData.Select(cad => cad.AttributeType)));
                Dependencies = GetVisibleDependencies(deps).Select(GetMainClassType).Distinct().ToArray();
				Usings = Dependencies.Select(x => x.Namespace).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
            }

            static ClassInfo[] GetRequiredNestedTypes(Type type, Type[] allowedCustomAttributes, Type[] fieldsDirectDeps)
			{
				return type
					.GetNestedTypes(CommonBindingFlags)
					.Where(x => IsRequiredNestedType(x, fieldsDirectDeps, allowedCustomAttributes))
                    .Where(x => x.CustomAttributes.All(y => y.AttributeType != typeof(DisableReference)))
					.Select(x => new ClassInfo(x, allowedCustomAttributes, false))
					.ToArray();
			}

            static CustomAttributeData[] GetCustomAttributesData(MemberInfo member, Type[] allowedCustomAttributes)
			{
				return member
					.GetCustomAttributesData()
					.Where(x => (x.AttributeType.Namespace?.StartsWith("Unity") ?? false) || allowedCustomAttributes.Contains(x.AttributeType))
					.ToArray();
			}

            static Type GetMainClassType(Type type)
			{
				return type.IsNested ? Type.GetType(type.FullName.Substring(0, type.FullName.IndexOf("+")) + ", " + type.Assembly) : type;
			}

            static IEnumerable<Type> GetVisibleDependencies(IEnumerable<Type> types)
			{
				var knownTypes = new HashSet<Type>(types);
				var stack = new Stack<Type>(knownTypes);
				while (stack.Count > 0)
				{
					var type = stack.Pop();
					yield return type;
					if (!type.IsGenericType)
						continue;
					var args = type.GetGenericArguments();
					foreach (var arg in args.Except(knownTypes))
						stack.Push(arg);
					knownTypes.UnionWith(args);
				}
			}

            static ClassFieldInfo[] GetTypeOwnFields(Type type, Type[] allowedCustomAttributes)
			{
                string[] baseTypeFieldNames = type.BaseType?.GetFields(CommonBindingFlags).Select(x => x.Name).ToArray() ?? Array.Empty<string>();
				return type
					.GetFields(CommonBindingFlags)
					.Where(x => !baseTypeFieldNames.Contains(x.Name))
                    .Where(x => x.CustomAttributes.All(y => y.AttributeType != typeof(DisableReference)))
					.Select(x => new ClassFieldInfo(x, allowedCustomAttributes))
					.Where(IsRequiredField)
					.ToArray();
			}

            static bool IsRequiredField(ClassFieldInfo member)
			{
				var customAttributes = member.CustomAttributesData;
				return member.Type.BaseType != typeof(MulticastDelegate) && (member.IsPublic || customAttributes.Length > 0);
			}

            static bool IsRequiredNestedType(Type type, IEnumerable<Type> requiredTypes, IEnumerable<Type> allowedCustomAttributes)
			{
				return requiredTypes.Contains(type)
					|| (type.IsVisible || allowedCustomAttributes.Any(type.IsDefined))
					&& type.BaseType != typeof(MulticastDelegate)
					&& !type.Name.StartsWith("Legacy_");
			}

			public class ClassFieldInfo
			{
				public Type Type;
				public bool IsPublic;
				public string Name;
				public CustomAttributeData[] CustomAttributesData;
                readonly FieldInfo FieldInfo;

				public ClassFieldInfo(FieldInfo field, Type[] allowedCustomAttributes)
				{
					Type = field.FieldType;
					IsPublic = field.IsPublic;
					Name = field.Name;
					FieldInfo = field;
					CustomAttributesData = GetCustomAttributesData(field, allowedCustomAttributes);
				}

				public static implicit operator FieldInfo(ClassFieldInfo fieldInfo) => fieldInfo.FieldInfo;
			}
		}
	}
}
