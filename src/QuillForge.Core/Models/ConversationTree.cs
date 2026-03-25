using Microsoft.Extensions.Logging;

namespace QuillForge.Core.Models;

/// <summary>
/// A tree-structured conversation where each message is a node with a parent and children.
/// Supports forking, variant creation, and safe deletion. All mutation is thread-safe.
/// </summary>
public sealed class ConversationTree
{
    private readonly Dictionary<Guid, MessageNode> _nodes = [];
    private readonly Lock _lock = new();
    private readonly ILogger<ConversationTree> _logger;

    private Guid _activeLeafId;
    private string _name;

    public ConversationTree(Guid sessionId, string name, ILogger<ConversationTree> logger)
    {
        _logger = logger;
        SessionId = sessionId;
        _name = name;

        var rootNode = new MessageNode
        {
            Id = Guid.CreateVersion7(),
            ParentId = null,
            Role = "system",
            Content = new MessageContent("conversation_root"),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _nodes[rootNode.Id] = rootNode;
        RootId = rootNode.Id;
        _activeLeafId = rootNode.Id;

        _logger.LogDebug("Created conversation tree {SessionId} with root {RootId}", sessionId, RootId);
    }

    /// <summary>
    /// Internal constructor for deserialization. Caller is responsible for providing valid state.
    /// </summary>
    internal ConversationTree(
        Guid sessionId,
        string name,
        Guid rootId,
        Guid activeLeafId,
        Dictionary<Guid, MessageNode> nodes,
        ILogger<ConversationTree> logger)
    {
        _logger = logger;
        SessionId = sessionId;
        _name = name;
        RootId = rootId;
        _activeLeafId = activeLeafId;
        _nodes = nodes;
    }

    public Guid SessionId { get; }
    public Guid RootId { get; }

    public string Name
    {
        get { lock (_lock) { return _name; } }
        set { lock (_lock) { _name = value; } }
    }

    public Guid ActiveLeafId
    {
        get { lock (_lock) { return _activeLeafId; } }
        set
        {
            lock (_lock)
            {
                if (!_nodes.ContainsKey(value))
                {
                    throw new ArgumentException($"Node {value} does not exist in the tree.");
                }
                _activeLeafId = value;
            }
        }
    }

    public int Count
    {
        get { lock (_lock) { return _nodes.Count; } }
    }

    /// <summary>
    /// Returns a snapshot of all nodes. Safe for enumeration — the dictionary is a copy.
    /// </summary>
    public IReadOnlyDictionary<Guid, MessageNode> GetSnapshot()
    {
        lock (_lock)
        {
            return new Dictionary<Guid, MessageNode>(_nodes);
        }
    }

    /// <summary>
    /// Looks up a single node by ID.
    /// </summary>
    public MessageNode? GetNode(Guid nodeId)
    {
        lock (_lock)
        {
            return _nodes.GetValueOrDefault(nodeId);
        }
    }

    /// <summary>
    /// Appends a new message as a child of the given parent node and sets it as the active leaf.
    /// </summary>
    public MessageNode Append(Guid parentId, string role, MessageContent content, MessageMetadata? metadata = null)
    {
        lock (_lock)
        {
            if (!_nodes.TryGetValue(parentId, out var parent))
            {
                throw new ArgumentException($"Parent node {parentId} does not exist.");
            }

            var newNode = new MessageNode
            {
                Id = Guid.CreateVersion7(),
                ParentId = parentId,
                Role = role,
                Content = content,
                CreatedAt = DateTimeOffset.UtcNow,
                Metadata = metadata,
            };

            // Update parent's child list (creates a new IReadOnlyList)
            _nodes[parentId] = parent with { ChildIds = [.. parent.ChildIds, newNode.Id] };
            _nodes[newNode.Id] = newNode;
            _activeLeafId = newNode.Id;

            _logger.LogDebug(
                "Appended {Role} node {NodeId} to parent {ParentId} in session {SessionId}",
                role, newNode.Id, parentId, SessionId);

            return newNode;
        }
    }

    /// <summary>
    /// Creates a variant (sibling) of an existing node — same parent, different content.
    /// Used for regeneration. Returns the new node and sets it as active leaf.
    /// </summary>
    public MessageNode CreateVariant(Guid existingNodeId, MessageContent content, MessageMetadata? metadata = null)
    {
        lock (_lock)
        {
            if (!_nodes.TryGetValue(existingNodeId, out var existing))
            {
                throw new ArgumentException($"Node {existingNodeId} does not exist.");
            }

            if (existing.ParentId is null)
            {
                throw new InvalidOperationException("Cannot create a variant of the root node.");
            }

            var variant = new MessageNode
            {
                Id = Guid.CreateVersion7(),
                ParentId = existing.ParentId,
                Role = existing.Role,
                Content = content,
                CreatedAt = DateTimeOffset.UtcNow,
                Metadata = metadata,
            };

            var parent = _nodes[existing.ParentId.Value];
            _nodes[existing.ParentId.Value] = parent with { ChildIds = [.. parent.ChildIds, variant.Id] };
            _nodes[variant.Id] = variant;
            _activeLeafId = variant.Id;

            _logger.LogDebug(
                "Created variant {VariantId} of node {ExistingId} in session {SessionId}",
                variant.Id, existingNodeId, SessionId);

            return variant;
        }
    }

    /// <summary>
    /// Walks from a leaf node to the root, returning the linear thread in root→leaf order.
    /// </summary>
    public IReadOnlyList<MessageNode> GetThread(Guid? leafId = null)
    {
        lock (_lock)
        {
            var targetLeaf = leafId ?? _activeLeafId;
            var thread = new List<MessageNode>();
            var currentId = (Guid?)targetLeaf;

            while (currentId is not null)
            {
                if (!_nodes.TryGetValue(currentId.Value, out var node))
                {
                    _logger.LogWarning(
                        "Broken chain: node {NodeId} not found while walking thread in session {SessionId}",
                        currentId.Value, SessionId);
                    break;
                }
                thread.Add(node);
                currentId = node.ParentId;
            }

            thread.Reverse();
            return thread;
        }
    }

    /// <summary>
    /// Returns the active thread as an ordered list, excluding the synthetic root node.
    /// Suitable for building LLM conversation context.
    /// </summary>
    public IReadOnlyList<MessageNode> ToFlatThread()
    {
        var thread = GetThread();
        // Skip the synthetic root node
        return thread.Count > 1 ? thread.Skip(1).ToList() : [];
    }

    /// <summary>
    /// Deletes a node and all orphaned descendants (children that have no other path to root).
    /// Cannot delete the root node.
    /// </summary>
    public int Delete(Guid nodeId)
    {
        lock (_lock)
        {
            if (nodeId == RootId)
            {
                throw new InvalidOperationException("Cannot delete the root node.");
            }

            if (!_nodes.TryGetValue(nodeId, out var node))
            {
                return 0;
            }

            // Remove from parent's child list
            if (node.ParentId is not null && _nodes.TryGetValue(node.ParentId.Value, out var parent))
            {
                _nodes[node.ParentId.Value] = parent with
                {
                    ChildIds = parent.ChildIds.Where(id => id != nodeId).ToList()
                };
            }

            // Collect all descendants to remove
            var toRemove = new List<Guid>();
            CollectDescendants(nodeId, toRemove);

            foreach (var id in toRemove)
            {
                _nodes.Remove(id);
            }

            // If active leaf was deleted, move to parent or root
            if (toRemove.Contains(_activeLeafId))
            {
                _activeLeafId = node.ParentId ?? RootId;
                _logger.LogDebug(
                    "Active leaf moved to {NewLeafId} after deletion in session {SessionId}",
                    _activeLeafId, SessionId);
            }

            _logger.LogDebug(
                "Deleted {Count} nodes starting from {NodeId} in session {SessionId}",
                toRemove.Count, nodeId, SessionId);

            return toRemove.Count;
        }
    }

    private void CollectDescendants(Guid nodeId, List<Guid> collected)
    {
        collected.Add(nodeId);
        if (_nodes.TryGetValue(nodeId, out var node))
        {
            foreach (var childId in node.ChildIds)
            {
                CollectDescendants(childId, collected);
            }
        }
    }
}
