using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnoEdit.Document;

namespace UnoEdit.Core.Tests;

[TestFixture]
public sealed class ThreadOwnershipTests
{
    [Test]
    public void WorkerOwnedDocumentSupportsLineCollectionsAndConnectedSegments()
    {
        var result = Task.Run(() =>
        {
            var document = new TextDocument("first\r\nsecond\nthird");
            var segments = new TextSegmentCollection<TextSegment>(document);
            var segment = new TextSegment { StartOffset = 7, Length = 6 };
            segments.Add(segment);
            document.Insert(0, "header\n");
            var lines = document.Lines.ToArray();
            return (document.LineCount, document.Lines.Count, lines.Length,
                document.Lines.IndexOf(lines[2]), segments.FirstSegment.StartOffset);
        }).GetAwaiter().GetResult();
        Assert.That(result, Is.EqualTo((4, 4, 4, 2, 14)));
    }

    [Test]
    public void OwnershipTransferAlsoTransfersLineCollectionAccess()
    {
        var document = new TextDocument("original\n");
        var lines = document.Lines;
        document.SetOwnerThread(null);
        Task.Run(() =>
        {
            document.SetOwnerThread(Thread.CurrentThread);
            Assert.That(lines.Count, Is.EqualTo(2));
            document.Insert(document.TextLength, "worker");
            Assert.That(lines.Last().Length, Is.EqualTo(6));
            document.SetOwnerThread(null);
        }).GetAwaiter().GetResult();
        document.SetOwnerThread(Thread.CurrentThread);
        Assert.That(document.Text, Is.EqualTo("original\nworker"));
        Assert.That(lines.Count, Is.EqualTo(2));
    }

    [Test]
    public void ForeignThreadCannotReadCapturedLiveLineCollection()
    {
        var document = new TextDocument("one\ntwo");
        var lines = document.Lines;
        Task.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => _ = lines.Count);
            Assert.Throws<InvalidOperationException>(() => _ = lines[0]);
            Assert.Throws<InvalidOperationException>(() => lines.GetEnumerator());
            Assert.Throws<InvalidOperationException>(() => new TextSegmentCollection<TextSegment>(document));
        }).GetAwaiter().GetResult();
    }

    [Test]
    public void SnapshotRemainsUsableAcrossOwnershipTransferAndEdits()
    {
        var document = new TextDocument("alpha\r\nbeta\ngamma");
        var snapshot = document.CreateSnapshot();
        document.SetOwnerThread(null);
        Task.Run(() =>
        {
            document.SetOwnerThread(Thread.CurrentThread);
            document.Replace(0, document.TextLength, "replacement");
            document.SetOwnerThread(null);
        }).GetAwaiter().GetResult();
        document.SetOwnerThread(Thread.CurrentThread);
        Assert.That(snapshot.Text, Is.EqualTo("alpha\r\nbeta\ngamma"));
        using var reader = snapshot.CreateReader();
        Assert.That(reader.ReadToEnd(), Is.EqualTo(snapshot.Text));
        Assert.That(document.Text, Is.EqualTo("replacement"));
    }

    [Test]
    public void CoreHasNoUIFrameworkAssemblyDependency()
    {
        var references = typeof(TextDocument).Assembly.GetReferencedAssemblies().Select(n => n.Name).ToArray();
        Assert.That(references.Any(n => n.StartsWith("Avalonia", StringComparison.Ordinal)), Is.False);
        Assert.That(references.Any(n => n.StartsWith("Uno.WinUI", StringComparison.Ordinal)), Is.False);
        Assert.That(references.Any(n => n.StartsWith("Microsoft.UI", StringComparison.Ordinal)), Is.False);
    }
}
