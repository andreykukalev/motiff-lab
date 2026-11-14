using MotiffLab.MusicCore.Import.Scan;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>A score's recognition told by its pages' real answers, never by time alone (v0.5.6, #533 section 1).</summary>
public class PageProgressTests
{
    [Fact]
    public void The_apps_send_one_page_a_request()
    {
        Assert.Equal(1, ScanParts.PagesARequest);
        Assert.Equal(8, ScanParts.Plan(8, ScanParts.PagesARequest).Count);
        Assert.All(ScanParts.Plan(8, ScanParts.PagesARequest), part => Assert.Equal(1, part.Count));
    }

    [Fact]
    public void Pages_read_are_done_the_page_sent_is_current_and_the_rest_wait()
    {
        var progress = new PageProgress(8);

        Assert.Equal(1, progress.Current);
        Assert.Equal([PageState.Current, .. Enumerable.Repeat(PageState.Waiting, 7)], progress.States);

        progress = progress.Read(30_000).Read(40_000);

        Assert.Equal(2, progress.Done);
        Assert.Equal(3, progress.Current);
        Assert.Equal([PageState.Done, PageState.Done, PageState.Current, PageState.Waiting, PageState.Waiting, PageState.Waiting, PageState.Waiting, PageState.Waiting], progress.States);
    }

    [Fact]
    public void Every_page_back_leaves_none_current_and_more_answers_change_nothing()
    {
        PageProgress progress = new PageProgress(2).Read(1_000).Read(1_000);

        Assert.Null(progress.Current);
        Assert.All(progress.States, state => Assert.Equal(PageState.Done, state));
        Assert.Same(progress, progress.Read(5_000));
    }

    [Fact]
    public void There_is_no_estimate_before_the_first_page_and_then_only_from_the_pages_measured()
    {
        var progress = new PageProgress(8);
        Assert.Null(progress.MinutesLeft);

        // 30 s and 40 s: 35 s a page, for the six pages not back yet, is three and a half minutes, so about four.
        progress = progress.Read(30_000).Read(40_000);
        Assert.Equal(4, progress.MinutesLeft);

        Assert.Equal(1, new PageProgress(2).Read(2_000).MinutesLeft);
        Assert.Null(new PageProgress(1).Read(2_000).MinutesLeft);
    }

    [Fact]
    public void The_time_for_pages_left_comes_from_the_mean_measured_rounded_up_to_minutes()
    {
        Assert.Null(PageProgress.MinutesFor([], 10));
        Assert.Null(PageProgress.MinutesFor([30_000], 0));
        Assert.Equal(5, PageProgress.MinutesFor([30_000], 10));
        Assert.Equal(6, PageProgress.MinutesFor([30_000, 32_000], 10));
        Assert.Equal(1, PageProgress.MinutesFor([1], 1));
    }

    [Fact]
    public void A_score_has_at_least_one_page() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageProgress(0));
}