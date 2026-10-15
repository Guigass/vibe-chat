using FluentAssertions;
using VibeChat.Infrastructure;
using VibeChat.Search;

namespace VibeChat.UnitTests;

public sealed class SemanticSearchTests
{
    [Fact]
    public void Similar_phrases_outrank_unrelated_text()
    {
        var left = SemanticEmbedding.Embed("backup do postgres no domingo");
        var similar = SemanticEmbedding.Cosine(left, SemanticEmbedding.Embed("postgres backup domingo"));
        var unrelated = SemanticEmbedding.Cosine(left, SemanticEmbedding.Embed("ola pessoal bom dia"));

        similar.Should().BeGreaterThan(unrelated);
        similar.Should().BeGreaterThan(SemanticPolicies.MinScore);
        unrelated.Should().BeLessThan(SemanticPolicies.MinScore);
    }

    [Fact]
    public void Answer_lines_are_exactly_the_citation_excerpts()
    {
        var citations = new[]
        {
            new SemanticCitation(Guid.NewGuid(), "message", Guid.NewGuid(), Guid.NewGuid(), 4, "postgres backup domingo", 0.8),
            new SemanticCitation(Guid.NewGuid(), "page", Guid.NewGuid(), null, null, "runbook de restore", 0.5),
        };

        var answer = SemanticAnswerComposer.Compose(citations);

        answer.Evidence.Should().Be(SemanticPolicies.EvidenceCited);
        answer.Text.Should().Be("[1] postgres backup domingo\n[2] runbook de restore");
        answer.Confidence.Should().Be(0.8);
    }

    [Fact]
    public void Empty_retrieval_is_a_valid_no_evidence_answer()
    {
        var answer = SemanticAnswerComposer.Compose([]);

        answer.Evidence.Should().Be(SemanticPolicies.EvidenceNone);
        answer.Text.Should().BeNull();
        answer.Confidence.Should().Be(0);
    }

    [Fact]
    public void Chunk_hash_changes_when_the_source_changes()
    {
        var original = SemanticChunker.Split("postgres backup runbook domingo");
        var edited = SemanticChunker.Split("mensagem sem relação com o tema");

        SemanticEmbedding.Hash(original[0]).Should().NotBe(SemanticEmbedding.Hash(edited[0]));
    }

    [Fact]
    public void Qdrant_hit_parser_reads_point_ids_only()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var json = "{\"result\":[{\"id\":\"" + id + "\",\"score\":0.9}]}";

        QdrantSemanticIndex.ParsePointIds(json).Should().Equal(id);
    }
}
