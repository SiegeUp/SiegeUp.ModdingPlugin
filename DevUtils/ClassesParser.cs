using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace SiegeUp.ModdingPlugin.DevUtils
{
	public class ClassesParser
	{
		public Type[] AllowedCustomAttributes { get; private set; }

		public ClassesParser()
		{
			AllowedCustomAttributes = new[] { typeof(SerializeField) };
		}

		public ClassesParser SetAllowedCustomAttributes(params Type[] allowedCustomAttributes)
		{
			AllowedCustomAttributes = allowedCustomAttributes ?? Array.Empty<Type>();
			if (!AllowedCustomAttributes.Contains(typeof(SerializeField)))
				AllowedCustomAttributes = AllowedCustomAttributes.Append(typeof(SerializeField)).ToArray();
			return this;
		}

		public void Parse(string outputFolder, Type filterAttributeType)
		{
			Assembly mainAssembly = null;
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
				if (assembly.GetName().Name == "Assembly-CSharp")
					mainAssembly = assembly;
			var types = mainAssembly.GetTypes().Where(x => x.IsDefined(filterAttributeType, false)).ToArray();
			Parse(outputFolder, types);
		}

		public void Parse(string outputFolder, Type[] types)
		{
			if (File.Exists(outputFolder))
				outputFolder = Path.GetDirectoryName(outputFolder);
			Directory.CreateDirectory(outputFolder);
			var time = DateTime.Now;
			var classesInfo = GetClassesInfo(types);
			Parallel.ForEach(classesInfo, (data) => SerializeClass(outputFolder, data));
			Debug.Log($"Scripts parsing was completed in {(DateTime.Now - time).TotalMilliseconds}ms. Created {classesInfo.Count} files");
		}

		private HashSet<ClassInfo> GetClassesInfo(Type[] types)
		{
			var result = new HashSet<ClassInfo>();
			var knownTypes = new HashSet<Type>(types);
			var stack = new Stack<Type>(types);
			while (stack.Count > 0)
			{
				var type = stack.Pop();
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

		private static void SerializeClass(string outputFolder, ClassInfo classInfo)
		{
			using (StreamWriter sw = new StreamWriter(Path.Combine(outputFolder, classInfo.Type + ".cs")))
				SerializeClassInfo(sw, classInfo);
		}

		private static void SerializeClassInfo(StreamWriter output, ClassInfo classInfo, string indent = "")
		{
			if (classInfo.Type.BaseType == typeof(Attribute))
			{
				CopyAttributeFile(output, classInfo.Type);
				return;
			}
			if (!classInfo.Type.IsNested && classInfo.Usings.Length > 0)
			{
				SerializeClassUsings(output, classInfo.Usings);
				output.WriteLine();
			}
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

		private static void CopyAttributeFile(StreamWriter output, Type type)
		{
			var projectDir = Path.Combine(Assembly.GetExecutingAssembly().Location, "..", "..", "..");
			var files = Directory.GetFiles(projectDir, $"{type.Name}*.cs", SearchOption.AllDirectories);
			using (StreamReader sr = new StreamReader(files.First()))
				output.Write(sr.ReadToEnd());
		}

		private static void SerializeEnumBody(StreamWriter output, ClassInfo classInfo, string indent)
		{
			foreach (var value in classInfo.Type.GetEnumNames())
			{
				output.Write(indent);
				output.Write(value);
				output.WriteLine(",");
			}
		}

		private static void SerializeTypeDeclaration(StreamWriter output, ClassInfo classInfo, string indent)
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

		private static void SerializeFields(StreamWriter output, ClassInfo.ClassFieldInfo[] members, string indent = "\t")
		{
			foreach (var member in members)
				SerializeField(output, member, indent);
		}

		private static void SerializeField(StreamWriter output, ClassInfo.ClassFieldInfo member, string indent = "\t")
		{
			SerializeAttributes(output, member.CustomAttributesData, indent);
			output.Write(indent);
			var modifiers = SerializeModifiersAndKeyWords(member);
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

		private static string SerializeMemberType(Type type, bool usePureName = false)
		{
			if (type.IsArray)
			{
				var subtype = type.GetElementType();
				var data = SerializeMemberType(subtype);
				var rankData = $"[{new string(',', type.GetArrayRank() - 1)}]";
				return subtype.IsArray ? data.Insert(data.IndexOf('['), rankData) : data + rankData;
			}
			else if (type.IsGenericType)
			{
				return $"{type.Name.Substring(0, type.Name.Length - 2)}<{string.Join(", ", type.GetGenericArguments().Select(x => SerializeMemberType(x)))}>";
			}
			else
			{
				if (usePureName)
					return type.Name;
				var typeParts = new[]
				{
					type.Namespace?.StartsWith("Unity") ?? false ? type.Namespace : "",
					type.DeclaringType?.Name ?? "",
					type.Name
				}.Where(x => x != "");
				return string.Join(".", typeParts);
			}
		}

		private static void SerializeAttributes(StreamWriter output, IEnumerable<CustomAttributeData> attributes, string indent = "")
		{
			foreach (var attr in attributes)
				SerializeAttribute(output, attr.AttributeType, attr.ConstructorArguments, indent);
		}

		private static void SerializeAttribute(StreamWriter output, Type attribute, IEnumerable<CustomAttributeTypedArgument> args, string indent)
		{
			output.Write($"{indent}[{attribute}");
			if (args != null)
				output.Write($"({String.Join(", ", GetAttributeArgsStringValues(args))})");
			output.WriteLine("]");
		}

		private static IEnumerable<string> GetAttributeArgsStringValues(IEnumerable<CustomAttributeTypedArgument> args)
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

		private static void SerializeClassUsings(StreamWriter output, IEnumerable<string> namespaces)
		{
			foreach (var ns in namespaces)
				output.WriteLine($"using {ns};");
		}

		private static string SerializeModifiersAndKeyWords(MemberInfo member)
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
				throw new NotImplementedException("Not supported member type");
			return string.Join(
				" ",
				new[] { isPublic ? "public" : "", isStatic ? "static" : "", isVirtual ? "virtual" : "", isAbstract ? "abstract" : "" }
					.Where(x => x != ""));
		}

		private static bool IsRequiredBaseType(Type type)
		{
			return type != typeof(object) && type != typeof(ValueType) && type != typeof(Enum);
		}

		private static bool IsSimpleType(Type type)
		{
			return !(type.IsArray || type.IsGenericType);
		}

		private static bool IsSystemType(Type type)
		{
			var assembly = type.Assembly.GetName().Name;
			return assembly == "mscorlib" || assembly.StartsWith("Unity") || assembly.StartsWith("System");
		}

		private class ClassInfo
		{
			public string[] Usings;
			public Type Type;
			public Type[] Dependencies;
			public bool ShouldBePartial;
			public bool IsSerializable;
			public CustomAttributeData[] CustomAttributesData;
			public ClassFieldInfo[] Fields;
			public ClassInfo[] NestedTypes;

			private static readonly BindingFlags CommonBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

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

			private static ClassInfo[] GetRequiredNestedTypes(Type type, Type[] allowedCustomAttributes, Type[] fieldsDirectDeps)
			{
				return type
					.GetNestedTypes(CommonBindingFlags)
					.Where(x => IsRequiredNestedType(x, fieldsDirectDeps, allowedCustomAttributes))
					.Select(x => new ClassInfo(x, allowedCustomAttributes, false))
					.ToArray();
			}

			private static CustomAttributeData[] GetCustomAttributesData(MemberInfo member, Type[] allowedCustomAttributes)
			{
				return member
					.GetCustomAttributesData()
					.Where(x => x.AttributeType.Namespace?.StartsWith("Unity") ?? false || allowedCustomAttributes.Contains(x.AttributeType))
					.ToArray();
			}

			private static Type GetMainClassType(Type type)
			{
				return type.IsNested ? Type.GetType(type.FullName.Substring(0, type.FullName.IndexOf("+")) + ", " + type.Assembly) : type;
			}

			private static IEnumerable<Type> GetVisibleDependencies(IEnumerable<Type> types)
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

			private static ClassFieldInfo[] GetTypeOwnFields(Type type, Type[] allowedCustomAttributes)
			{
				var baseTypeFieldNames = type.BaseType?.GetFields(CommonBindingFlags).Select(x => x.Name).ToArray() ?? Array.Empty<string>();
				return type
					.GetFields(CommonBindingFlags)
					.Where(x => !baseTypeFieldNames.Contains(x.Name))
					.Select(x => new ClassFieldInfo(x, allowedCustomAttributes))
					.Where(IsRequiredField)
					.ToArray();
			}

			private static bool IsRequiredField(ClassFieldInfo member)
			{
				var customAttributes = member.CustomAttributesData;
				return member.Type.BaseType != typeof(MulticastDelegate) && (member.IsPublic || customAttributes.Length > 0);
			}

			private static bool IsRequiredNestedType(Type type, Type[] requiredTypes, Type[] allowedCustomAttributes)
			{
				return requiredTypes.Contains(type)
					|| (type.IsVisible || allowedCustomAttributes.Any(x => type.IsDefined(x)))
					&& type.BaseType != typeof(MulticastDelegate)
					&& !type.Name.StartsWith("Legacy_");
			}

			public class ClassFieldInfo
			{
				public Type Type;
				public bool IsPublic;
				public string Name;
				public CustomAttributeData[] CustomAttributesData;
				private readonly FieldInfo FieldInfo;

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
