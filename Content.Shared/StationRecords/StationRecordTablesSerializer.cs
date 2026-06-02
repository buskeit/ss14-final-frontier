using Robust.Shared.IoC;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared.StationRecords;

public sealed class StationRecordTablesSerializer : ITypeSerializer<Dictionary<Type, Dictionary<uint, object>>, MappingDataNode>
{
    public ValidationNode Validate(ISerializationManager serializationManager,
        MappingDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        var mapping = new Dictionary<ValidationNode, ValidationNode>();

        foreach (var (recordTypeName, tableNode) in node.Children)
        {
            var type = serializationManager.ReflectionManager.YamlTypeTagLookup(typeof(object), recordTypeName);
            var keyNode = node.GetKeyNode(recordTypeName);

            if (type == null)
            {
                mapping.Add(new ErrorNode(keyNode, $"Could not resolve station record type: {recordTypeName}"),
                    new ValidatedValueNode(tableNode));
                continue;
            }

            if (tableNode is not MappingDataNode tableMapping)
            {
                mapping.Add(new ValidatedValueNode(keyNode),
                    new ErrorNode(tableNode, "Station record table must be a mapping."));
                continue;
            }

            var tableValidation = new Dictionary<ValidationNode, ValidationNode>();
            foreach (var (recordId, recordNode) in tableMapping.Children)
            {
                tableValidation.Add(
                    serializationManager.ValidateNode<uint>(tableMapping.GetKeyNode(recordId), context),
                    serializationManager.ValidateNode(type, recordNode, context));
            }

            mapping.Add(new ValidatedValueNode(keyNode), new ValidatedMappingNode(tableValidation));
        }

        return new ValidatedMappingNode(mapping);
    }

    public Dictionary<Type, Dictionary<uint, object>> Read(ISerializationManager serializationManager,
        MappingDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<Dictionary<Type, Dictionary<uint, object>>>? instanceProvider = null)
    {
        var tables = instanceProvider != null
            ? instanceProvider()
            : new Dictionary<Type, Dictionary<uint, object>>();

        foreach (var (recordTypeName, tableNode) in node.Children)
        {
            var type = serializationManager.ReflectionManager.YamlTypeTagLookup(typeof(object), recordTypeName);
            if (type == null || tableNode is not MappingDataNode tableMapping)
                continue;

            var table = new Dictionary<uint, object>();
            var keyNode = new ValueDataNode();

            foreach (var (recordId, recordNode) in tableMapping.Children)
            {
                keyNode.Value = recordId;
                var key = serializationManager.Read<uint>(keyNode, hookCtx, context);
                var record = serializationManager.Read(type, recordNode, hookCtx, context, notNullableOverride: true);

                if (record != null)
                    table[key] = record;
            }

            tables[type] = table;
        }

        return tables;
    }

    public DataNode Write(ISerializationManager serializationManager,
        Dictionary<Type, Dictionary<uint, object>> value,
        IDependencyCollection dependencies,
        bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        var mappingNode = new MappingDataNode();

        foreach (var (recordType, table) in value)
        {
            var tableNode = new MappingDataNode();

            foreach (var (recordId, record) in table)
            {
                var keyNode = serializationManager.WriteValue(recordId, alwaysWrite, context);
                if (keyNode is not ValueDataNode valueNode)
                    throw new NotSupportedException("Station record IDs must serialize to scalar YAML keys.");

                tableNode.Add(
                    valueNode.Value,
                    serializationManager.WriteValue(recordType, record, alwaysWrite, context, notNullableOverride: true));
            }

            mappingNode.Add(recordType.Name, tableNode);
        }

        return mappingNode;
    }
}
