using ParcelNumberGenerator.Domain;

namespace ParcelNumberGenerator.Tests;

/// <summary>
/// The pool arithmetic. Every case here is one the legacy implementation got wrong, or a
/// boundary it never considered.
/// </summary>
public sealed class NumberPoolTests
{
    [Fact]
    public void Capacity_counts_both_ends_of_the_range()
    {
        NumberPool pool = NumberPool.Create(new NumberRange(1, 10));

        // The legacy ElementsInRange returned `second - first + 1` on top of two counts that
        // were already inclusive, so every pool reported one number more than it held.
        Assert.Equal(10, pool.Capacity);
    }

    [Fact]
    public void Capacity_of_a_single_number_range_is_one()
    {
        Assert.Equal(1, NumberPool.Create(new NumberRange(7, 7)).Capacity);
    }

    [Fact]
    public void Capacity_excludes_excluded_numbers()
    {
        NumberPool pool = NumberPool.Create(new NumberRange(1, 100), [new NumberRange(30, 40)]);

        Assert.Equal(100 - 11, pool.Capacity);
    }

    [Fact]
    public void Overlapping_exclusions_are_merged_rather_than_double_counted()
    {
        NumberPool pool = NumberPool.Create(
            new NumberRange(1, 100),
            [new NumberRange(10, 20), new NumberRange(15, 25)]);

        Assert.Equal(100 - 16, pool.Capacity);
        Assert.Equal([new NumberRange(10, 25)], pool.Exclusions);
    }

    [Fact]
    public void Adjacent_exclusions_are_merged()
    {
        NumberPool pool = NumberPool.Create(
            new NumberRange(1, 100),
            [new NumberRange(10, 19), new NumberRange(20, 29)]);

        Assert.Equal([new NumberRange(10, 29)], pool.Exclusions);
    }

    [Fact]
    public void Exclusions_are_clipped_to_the_pool_and_may_fall_outside_it_entirely()
    {
        NumberPool pool = NumberPool.Create(
            new NumberRange(50, 100),
            [new NumberRange(1, 60), new NumberRange(200, 300)]);

        Assert.Equal([new NumberRange(50, 60)], pool.Exclusions);
        Assert.Equal(100 - 61 + 1, pool.Capacity);
    }

    [Fact]
    public void Exclusions_are_normalized_regardless_of_the_order_given()
    {
        NumberPool ascending = NumberPool.Create(
            new NumberRange(1, 100),
            [new NumberRange(10, 20), new NumberRange(60, 70)]);
        NumberPool descending = NumberPool.Create(
            new NumberRange(1, 100),
            [new NumberRange(60, 70), new NumberRange(10, 20)]);

        Assert.Equal(ascending.Exclusions, descending.Exclusions);
        Assert.Equal(ascending.Capacity, descending.Capacity);
    }

    [Fact]
    public void A_fully_excluded_pool_has_no_capacity()
    {
        NumberPool pool = NumberPool.Create(new NumberRange(1, 10), [new NumberRange(1, 10)]);

        Assert.Equal(0, pool.Capacity);
        Assert.Empty(pool.Segments);
        Assert.False(pool.Contains(5));
    }

    [Fact]
    public void NumberAt_skips_the_excluded_window()
    {
        NumberPool pool = NumberPool.Create(new NumberRange(1, 10), [new NumberRange(4, 6)]);

        int[] allocatable = [.. Enumerable.Range(0, (int)pool.Capacity).Select(index => pool.NumberAt(index))];

        Assert.Equal([1, 2, 3, 7, 8, 9, 10], allocatable);
    }

    [Fact]
    public void NumberAt_and_IndexOf_are_inverses_across_the_whole_pool()
    {
        NumberPool pool = NumberPool.Create(
            new NumberRange(1, 1000),
            [new NumberRange(100, 200), new NumberRange(500, 500), new NumberRange(900, 1000)]);

        for (long index = 0; index < pool.Capacity; index++)
        {
            Assert.Equal(index, pool.IndexOf(pool.NumberAt(index)));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void NumberAt_rejects_an_index_outside_the_pool(long index)
    {
        NumberPool pool = NumberPool.Create(new NumberRange(1, 10), [new NumberRange(4, 6)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => pool.NumberAt(index));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, false)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void Contains_covers_the_range_ends_and_the_exclusion(int number, bool expected)
    {
        NumberPool pool = NumberPool.Create(new NumberRange(1, 10), [new NumberRange(4, 6)]);

        Assert.Equal(expected, pool.Contains(number));
    }

    [Fact]
    public void IndexOf_returns_null_for_a_number_that_is_not_allocatable()
    {
        NumberPool pool = NumberPool.Create(new NumberRange(1, 10), [new NumberRange(4, 6)]);

        Assert.Null(pool.IndexOf(5));
        Assert.Null(pool.IndexOf(99));
    }

    [Fact]
    public void A_full_int_range_does_not_overflow_its_capacity()
    {
        NumberPool pool = NumberPool.Create(new NumberRange(0, int.MaxValue));

        // int.MaxValue + 1 values — the reason Capacity is a long. Counted in an int this
        // wraps to a negative capacity, and every bounds check downstream inverts.
        Assert.Equal(1L + int.MaxValue, pool.Capacity);
        Assert.Equal(int.MaxValue, pool.NumberAt(pool.Capacity - 1));
    }

    [Fact]
    public void An_exclusion_touching_int_MaxValue_does_not_wrap()
    {
        NumberPool pool = NumberPool.Create(
            new NumberRange(int.MaxValue - 10, int.MaxValue),
            [new NumberRange(int.MaxValue - 2, int.MaxValue)]);

        Assert.Equal(8, pool.Capacity);
        Assert.Equal(int.MaxValue - 3, pool.NumberAt(pool.Capacity - 1));
    }

    [Fact]
    public void An_inverted_range_is_rejected_at_construction()
    {
        Assert.Throws<ArgumentException>(() => new NumberRange(10, 1));
    }
}
