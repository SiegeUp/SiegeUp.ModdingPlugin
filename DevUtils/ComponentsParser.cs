using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace SiegeUp.ModdingPlugin.DevUtils
{
	[CreateAssetMenu]
	public class ComponentsParser : ScriptableObject
	{
		public static ComponentsParser Instance;

		private const string TempFolder = "temp_components";
		private static Type ComponentIdAttrType = Type.GetType("ComponentId, Assembly-CSharp");
		private static string ProjectDir;
		private static string OutDir;

		//todo usings serialization can be removed if type.FullName will be used everywhere
		//todo save default variables values
		[PostProcessBuildAttribute]
		public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
		{
			Debug.Log(pathToBuiltProject);
			string[] assets = AssetDatabase.FindAssets($"t:{nameof(ComponentsParser)}");
			if (assets.Length > 0)
			{
				string path = AssetDatabase.GUIDToAssetPath(assets[0]);
				Instance = AssetDatabase.LoadAssetAtPath<ComponentsParser>(path);
			}
			else
			{
				Debug.LogError("No ComponentsParser instance found. Create instance if you want to parse components");
				return;
			}
			Instance.Parse(pathToBuiltProject);
		}

		public void Parse(string buildPath)
		{
			Assembly mainAssembly = null;
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
				if (assembly.GetName().Name == "Assembly-CSharp")
					mainAssembly = assembly;
			var types = mainAssembly.GetTypes().Where(x => x.GetCustomAttributes(ComponentIdAttrType, false).Length > 0).ToArray();
			Parse(buildPath, types);
		}

		public void Parse(string buildPath, params Type[] types)
		{
			//ProjectDir = Path.Combine(Assembly.GetExecutingAssembly().Location, "..", "..", "..");
			if (File.Exists(buildPath))
				buildPath = Path.GetDirectoryName(buildPath);
			OutDir = Path.Combine(buildPath, "Scripts");
			Directory.CreateDirectory(OutDir);
			
			GC.Collect();
			var mem = GC.GetTotalMemory(true);
			var dt = DateTime.Now;
			
			var classesInfo = GetClassesInfo(types);
			Debug.Log($"parsing time: {(DateTime.Now - dt).TotalMilliseconds} mem: {(GC.GetTotalMemory(false) - mem) / 8f / 1024f}KB");
			Parallel.ForEach(classesInfo, (data) =>
			{
				using (StreamWriter sw = new StreamWriter(Path.Combine(OutDir, data.Type + ".cs")))
					ClassSerializer.SerializeClassInfo(sw, data);
			});
			Debug.Log($"serialization ({classesInfo.Count}) time: {(DateTime.Now - dt).TotalMilliseconds} mem: {(GC.GetTotalMemory(false) - mem) / 8f / 1024f}KB");
			Debug.Log($"Scripts parsing completed with a result of 'Succeeded' in {(DateTime.Now - dt).TotalMilliseconds}ms. Created {classesInfo.Count} files");
			//Directory.Delete(OutDir, true);
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
				var classInfo = new ClassInfo(type, types.Contains(type));
				foreach (var a in classInfo.Dependencies.Except(knownTypes))
					stack.Push(a);
				knownTypes.UnionWith(classInfo.Dependencies);
				result.Add(classInfo);
			}
			return result;
		}

		private bool IsSimpleType(Type type)
		{
			return !(type.IsArray || type.IsGenericType);
		}

		private bool IsSystemType(Type type)
		{
			var assembly = type.Assembly.GetName().Name;
			return assembly == "mscorlib" || assembly.StartsWith("Unity") || assembly.StartsWith("System");
		}

		private class ClassInfo
		{
			public Type Type;
			public Type BaseType;
			public ClassInfo[] NestedTypes;
			public bool HasSerializableAttribute;
			public CustomAttributeData[] CustomAttributes;
			public MemberInfo[] Variables;
			public Type[] Dependencies;
			public bool ShouldBePartial;
			public IEnumerable<string> Usings => Dependencies.Select(x => x.Namespace).Where(x => !string.IsNullOrEmpty(x)).Distinct();

			private static readonly BindingFlags FieldBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

			public ClassInfo(Type type, bool shouldBePartial)
			{
				ShouldBePartial = shouldBePartial;
				Type = type;
				NestedTypes = Type.GetNestedTypes().Where(IsImportantNestedType).Select(x => new ClassInfo(x, false)).ToArray();
				HasSerializableAttribute = (type.Attributes & TypeAttributes.Serializable) == TypeAttributes.Serializable;
				CustomAttributes = type.GetCustomAttributesData().Where(x => x.AttributeType.Namespace != null).ToArray();
				var fields = type.GetFields(FieldBindingFlags).Where(IsImportantField);
				var deps = new List<Type>();
				deps.AddRange(NestedTypes.SelectMany(x => x.Dependencies));
				if (IsRequiredBaseType(type.BaseType))
				{
					BaseType = type.BaseType;
					deps.Add(BaseType);
				}
				var baseTypeFieldNames = BaseType?.GetFields().Select(field => field.Name).ToArray() ?? Array.Empty<string>();
				Variables = fields.Where(x => !baseTypeFieldNames.Contains(x.Name)).ToArray();
				deps.AddRange(fields.Select(x => x.FieldType));
				deps.AddRange(CustomAttributes.Select(x => x.AttributeType));
				Dependencies = GetVisibleDependencies(deps).Select(GetMainClassType).Distinct().ToArray();
			}

			private Type GetMainClassType(Type type)
			{
				return type.IsNested ? Type.GetType(type.FullName.Substring(0, type.FullName.IndexOf("+")) + ", " + type.Assembly) : type;
			}

			private IEnumerable<Type> GetVisibleDependencies(IEnumerable<Type> types)
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
			private bool IsImportantField(FieldInfo member)
			{
				var customAttributes = member.GetCustomAttributesData();
				return member.FieldType.BaseType != typeof(MulticastDelegate)
					//&& customAttributes.All(x => x.AttributeType != typeof(HideInInspector))
					&& (member.IsPublic || customAttributes.Any(x => x.AttributeType == typeof(SerializeField)));
			}

			private bool IsImportantNestedType(Type type)
			{
				return type.BaseType != typeof(MulticastDelegate) && !type.Name.StartsWith("Legacy_");
			}

			private bool IsRequiredBaseType(Type type)
			{
				return type != typeof(object) && type != typeof(ValueType) && type != typeof(Enum);
			}
		}

		private static class ClassSerializer
		{
			//todo copy whole attributes file
			//todo fix abstract classes serialzation
			public static void SerializeClassInfo(StreamWriter output, ClassInfo classInfo, string indent = "")
			{
				if (!classInfo.Type.IsNested)
				{
					SerializeClassUsings(output, classInfo.Usings);
					output.WriteLine();
				}
				SerializeAttributes(output, classInfo.CustomAttributes, indent);
				if (classInfo.HasSerializableAttribute)
					SerializeAttribute(output, typeof(SerializableAttribute), null, indent);
				SerializeTypeDeclaration(output, classInfo, indent);
				output.WriteLine(indent + "{");
				if (classInfo.Type.IsEnum)
				{
					SerializeEnumBody(output, classInfo, indent + "\t");
				}
				else if (!classInfo.Type.IsInterface)
				{
					SerializeClassMembers(output, classInfo.Variables, indent + "\t");
					foreach (var nestedType in classInfo.NestedTypes)
					{
						output.WriteLine();
						SerializeClassInfo(output, nestedType, indent + "\t");
					}
				}
				output.Write(indent);
				output.WriteLine("}");
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
				if (classInfo.BaseType != null)
					output.Write($" : {classInfo.BaseType}");
				output.WriteLine();
			}

			private static void SerializeClassMembers(StreamWriter output, MemberInfo[] members, string indent = "\t")
			{
				foreach (var member in members)
				{
					SerializeClassMember(output, member, indent);
					output.WriteLine();
				}
			}

			private static void SerializeClassMember(StreamWriter output, MemberInfo member, string indent = "\t")
			{
				Type type;
				var serializeAttr = member.GetCustomAttribute<SerializeField>();
				if (serializeAttr != null)
					SerializeAttribute(output, serializeAttr.GetType(), null, indent);
				output.Write(indent);
				output.Write(SerializeModifiersAndKeyWords(member));

				string additionalMemberData;
				if (member is FieldInfo field)
				{
					type = field.FieldType;
					additionalMemberData = ";";
				}
				else
				{
					throw new NotImplementedException("Unsupported class member type");
				}

				output.Write(" ");
				output.Write(SerializeMemberType(type));
				output.Write(" ");
				output.Write(member.Name);
				output.Write(additionalMemberData);
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
					output.Write($"({String.Join(", ", args)})");
				output.WriteLine("]");
			}

			private static void SerializeClassUsings(StreamWriter output, IEnumerable<string> namespaces)
			{
				output.WriteLine(string.Join("\n", namespaces.Select(x => $"using {x};"))); //todo check prefomance
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
		}
	}
}
