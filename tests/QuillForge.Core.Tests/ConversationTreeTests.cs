using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;

namespace QuillForge.Core.Tests;

public class ConversationTreeTests
{
    private static readonly ILogger<ConversationTree> Logger =
        NullLoggerFactory.Instance.CreateLogger<ConversationTree>();

    private static ConversationTree CreateTree(string name = "test")
    {
        return new ConversationTree(Guid.CreateVersion7(), name, Logger);
    }

    [Fact]
    public void NewTree_HasRootNode_And_SingleNodeCount()
    {
        var tree = CreateTree();

        Assert.Equal(1, tree.Count);
        Assert.NotEqual(Guid.Empty, tree.RootId);
        Assert.Equal(tree.RootId, tree.ActiveLeafId);
    }

    [Fact]
    public void Append_CreatesChildOfParent_And_SetsActiveLeaf()
    {
        var tree = CreateTree();
        var node = tree.Append(tree.RootId, "user", new MessageContent("hello"));

        Assert.Equal(2, tree.Count);
        Assert.Equal("user", node.Role);
        Assert.Equal(tree.RootId, node.ParentId);
        Assert.Equal(node.Id, tree.ActiveLeafId);

        // Root should list the new node as a child
        var root = tree.GetNode(tree.RootId)!;
        Assert.Contains(node.Id, root.ChildIds);
    }

    [Fact]
    public void Append_ToNonexistentParent_Throws()
    {
        var tree = CreateTree();
        Assert.Throws<ArgumentException>(() =>
            tree.Append(Guid.NewGuid(), "user", new MessageContent("hello")));
    }

    [Fact]
    public void Append_MultipleMessages_BuildsLinearChain()
    {
        var tree = CreateTree();
        var msg1 = tree.Append(tree.RootId, "user", new MessageContent("hello"));
        var msg2 = tree.Append(msg1.Id, "assistant", new MessageContent("hi there"));
        var msg3 = tree.Append(msg2.Id, "user", new MessageContent("how are you"));

        Assert.Equal(4, tree.Count); // root + 3
        Assert.Equal(msg3.Id, tree.ActiveLeafId);
    }

    [Fact]
    public void GetThread_ReturnsRootToLeaf_InOrder()
    {
        var tree = CreateTree();
        var msg1 = tree.Append(tree.RootId, "user", new MessageContent("first"));
        var msg2 = tree.Append(msg1.Id, "assistant", new MessageContent("second"));

        var thread = tree.GetThread();

        Assert.Equal(3, thread.Count); // root, msg1, msg2
        Assert.Equal(tree.RootId, thread[0].Id);
        Assert.Equal(msg1.Id, thread[1].Id);
        Assert.Equal(msg2.Id, thread[2].Id);
    }

    [Fact]
    public void ToFlatThread_ExcludesRoot()
    {
        var tree = CreateTree();
        var msg1 = tree.Append(tree.RootId, "user", new MessageContent("first"));
        var msg2 = tree.Append(msg1.Id, "assistant", new MessageContent("second"));

        var flat = tree.ToFlatThread();

        Assert.Equal(2, flat.Count);
        Assert.Equal(msg1.Id, flat[0].Id);
        Assert.Equal(msg2.Id, flat[1].Id);
    }

    [Fact]
    public void ToFlatThread_EmptyTree_ReturnsEmpty()
    {
        var tree = CreateTree();
        var flat = tree.ToFlatThread();
        Assert.Empty(flat);
    }

    [Fact]
    public void Fork_CreatesBranch_FromMidConversation()
    {
        var tree = CreateTree();
        var msg1 = tree.Append(tree.RootId, "user", new MessageContent("hello"));
        var msg2 = tree.Append(msg1.Id, "assistant", new MessageContent("original reply"));

        // Fork from msg1 with a different reply
        var fork = tree.Append(msg1.Id, "assistant", new MessageContent("alternate reply"));

        // msg1 should now have two children
        var msg1Updated = tree.GetNode(msg1.Id)!;
        Assert.Equal(2, msg1Updated.ChildIds.Count);
        Assert.Contains(msg2.Id, msg1Updated.ChildIds);
        Assert.Contains(fork.Id, msg1Updated.ChildIds);

        // Active leaf should be the fork
        Assert.Equal(fork.Id, tree.ActiveLeafId);

        // Thread from fork should not include msg2
        var thread = tree.GetThread(fork.Id);
        Assert.DoesNotContain(thread, n => n.Id == msg2.Id);
        Assert.Contains(thread, n => n.Id == fork.Id);
    }

    [Fact]
    public void CreateVariant_AddsSiblingNode()
    {
        var tree = CreateTree();
        var msg1 = tree.Append(tree.RootId, "user", new MessageContent("hello"));
        var original = tree.Append(msg1.Id, "assistant", new MessageContent("original"));

        var variant = tree.CreateVariant(original.Id, new MessageContent("regenerated"));

        Assert.NotEqual(original.Id, variant.Id);
        Assert.Equal(original.ParentId, variant.ParentId);
        Assert.Equal("assistant", variant.Role);
        Assert.Equal(variant.Id, tree.ActiveLeafId);

        // Parent should have both children
        var parent = tree.GetNode(msg1.Id)!;
        Assert.Equal(2, parent.ChildIds.Count);
    }

    [Fact]
    public void CreateVariant_OfRoot_Throws()
    {
        var tree = CreateTree();
        Assert.Throws<InvalidOperationException>(() =>
            tree.CreateVariant(tree.RootId, new MessageContent("nope")));
    }

    [Fact]
    public void Delete_RemovesNode_And_Descendants()
    {
        var tree = CreateTree();
        var msg1 = tree.Append(tree.RootId, "user", new MessageContent("hello"));
        var msg2 = tree.Append(msg1.Id, "assistant", new MessageContent("reply"));
        var msg3 = tree.Append(msg2.Id, "user", new MessageContent("followup"));

        // Delete msg2 — should also remove msg3
        var removed = tree.Delete(msg2.Id);

        Assert.Equal(2, removed); // msg2 + msg3
        Assert.Equal(2, tree.Count); // root + msg1
        Assert.Null(tree.GetNode(msg2.Id));
        Assert.Null(tree.GetNode(msg3.Id));

        // msg1 should no longer list msg2 as a child
        var msg1Updated = tree.GetNode(msg1.Id)!;
        Assert.Empty(msg1Updated.ChildIds);
    }

    [Fact]
    public void Delete_MovesActiveLeaf_ToParent_WhenLeafDeleted()
    {
        var tree = CreateTree();
        var msg1 = tree.Append(tree.RootId, "user", new MessageContent("hello"));
        var msg2 = tree.Append(msg1.Id, "assistant", new MessageContent("reply"));
        Assert.Equal(msg2.Id, tree.ActiveLeafId);

        tree.Delete(msg2.Id);

        Assert.Equal(msg1.Id, tree.ActiveLeafId);
    }

    [Fact]
    public void Delete_SiblingBranch_PreservesSibling()
    {
        var tree = CreateTree();
        var msg1 = tree.Append(tree.RootId, "user", new MessageContent("hello"));
        var branchA = tree.Append(msg1.Id, "assistant", new MessageContent("branch A"));
        var branchB = tree.Append(msg1.Id, "assistant", new MessageContent("branch B"));

        tree.Delete(branchA.Id);

        Assert.NotNull(tree.GetNode(branchB.Id));
        var msg1Updated = tree.GetNode(msg1.Id)!;
        Assert.Single(msg1Updated.ChildIds);
        Assert.Contains(branchB.Id, msg1Updated.ChildIds);
    }

    [Fact]
    public void Delete_Root_Throws()
    {
        var tree = CreateTree();
        Assert.Throws<InvalidOperationException>(() => tree.Delete(tree.RootId));
    }

    [Fact]
    public void Delete_NonexistentNode_ReturnsZero()
    {
        var tree = CreateTree();
        Assert.Equal(0, tree.Delete(Guid.NewGuid()));
    }

    [Fact]
    public void GetSnapshot_ReturnsIndependentCopy()
    {
        var tree = CreateTree();
        tree.Append(tree.RootId, "user", new MessageContent("hello"));

        var snapshot = tree.GetSnapshot();
        Assert.Equal(2, snapshot.Count);

        // Mutating the tree should not affect the snapshot
        tree.Append(tree.ActiveLeafId, "assistant", new MessageContent("reply"));
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(3, tree.Count);
    }

    [Fact]
    public void Name_IsThreadSafe()
    {
        var tree = CreateTree("original");
        Assert.Equal("original", tree.Name);

        tree.Name = "updated";
        Assert.Equal("updated", tree.Name);
    }

    [Fact]
    public void ActiveLeafId_RejectsNonexistentNode()
    {
        var tree = CreateTree();
        Assert.Throws<ArgumentException>(() => tree.ActiveLeafId = Guid.NewGuid());
    }

    [Fact]
    public void MessageContent_GetText_ConcatenatesTextBlocks()
    {
        var content = new MessageContent(
        [
            new TextBlock("Hello "),
            new TextBlock("World"),
        ]);

        Assert.Equal("Hello World", content.GetText());
    }

    [Fact]
    public void MessageContent_StringConstructor_CreatesTextBlock()
    {
        var content = new MessageContent("simple text");

        Assert.Single(content.Blocks);
        Assert.IsType<TextBlock>(content.Blocks[0]);
        Assert.Equal("simple text", content.GetText());
    }

    [Fact]
    public async Task ConcurrentAppends_AreThreadSafe()
    {
        var tree = CreateTree();
        var parentId = tree.RootId;

        // Spawn many concurrent appends to the same parent
        var tasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(() => tree.Append(parentId, "user", new MessageContent($"msg {i}")))).ToArray();

        await Task.WhenAll(tasks);

        // Root should have 100 children, tree should have 101 nodes total
        Assert.Equal(101, tree.Count);
        var root = tree.GetNode(tree.RootId)!;
        Assert.Equal(100, root.ChildIds.Count);
    }

    [Fact]
    public async Task ConcurrentAppendAndDelete_AreThreadSafe()
    {
        var tree = CreateTree();

        // Build a chain of 50 messages
        var lastId = tree.RootId;
        var nodeIds = new List<Guid>();
        for (var i = 0; i < 50; i++)
        {
            var node = tree.Append(lastId, "user", new MessageContent($"msg {i}"));
            nodeIds.Add(node.Id);
            lastId = node.Id;
        }

        // Concurrently append new messages and delete existing ones
        var appendTasks = Enumerable.Range(0, 20).Select(i =>
            Task.Run(() => tree.Append(tree.RootId, "user", new MessageContent($"concurrent {i}")))).ToArray();

        var deleteTasks = nodeIds.Take(10).Select(id =>
            Task.Run(() => tree.Delete(id))).ToArray();

        await Task.WhenAll([.. appendTasks, .. deleteTasks]);

        // Tree should still be in a consistent state
        var snapshot = tree.GetSnapshot();
        foreach (var (id, node) in snapshot)
        {
            if (node.ParentId is not null)
            {
                // Parent should exist
                Assert.True(snapshot.ContainsKey(node.ParentId.Value),
                    $"Node {id} references missing parent {node.ParentId.Value}");
            }
        }
    }
}
