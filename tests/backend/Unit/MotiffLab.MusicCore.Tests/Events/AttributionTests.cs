using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Tests.Events;

public class AttributionTests
{
    [Fact]
    public void Imported_and_user_values_are_certain()
    {
        Assert.Equal(new Attribution(Provenance.Imported, 1), Attribution.Imported);
        Assert.Equal(1, Attribution.UserConfirmed.Confidence);
        Assert.Equal(Provenance.UserEdited, Attribution.UserEdited.Provenance);
    }

    [Theory]
    [InlineData(Provenance.Imported)]
    [InlineData(Provenance.UserConfirmed)]
    [InlineData(Provenance.UserEdited)]
    public void Certain_provenances_reject_lower_confidence(Provenance provenance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Attribution(provenance, 0.9));
    }

    [Fact]
    public void Inferred_and_detected_values_carry_their_confidence()
    {
        Attribution hand = Attribution.Inferred(0.72);

        Assert.Equal(Provenance.Inferred, hand.Provenance);
        Assert.Equal(0.72, hand.Confidence);
        Assert.Equal(0, Attribution.Detected(0).Confidence);
        Assert.Equal(1, Attribution.Inferred(1).Confidence);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Confidence_must_be_between_zero_and_one(double confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attribution.Inferred(confidence));
    }

    [Fact]
    public void Unknown_provenance_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Attribution((Provenance)99, 1));
    }

    [Fact]
    public void Attributed_value_requires_an_attribution()
    {
        Assert.Throws<ArgumentNullException>(() => new Attributed<Hand>(Hand.Left, null!));
        Assert.Equal(
            new Attributed<Hand>(Hand.Left, Attribution.Inferred(0.8)),
            new Attributed<Hand>(Hand.Left, Attribution.Inferred(0.8)));
    }
}