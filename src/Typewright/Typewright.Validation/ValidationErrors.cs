using System.Collections;

namespace Typewright.Validation;

/// <summary>
/// An accumulating collection of <see cref="ValidationFailure"/> values with path
/// scoping. Nested models push a path segment (for example <c>lineItems[2]</c>) so child
/// failures are prefixed with their location without the child knowing where it sits.
/// </summary>
public sealed class ValidationErrors : IReadOnlyList<ValidationFailure>
{
    private readonly List<ValidationFailure> _failures = [];
    private string _currentPath = string.Empty;

    /// <inheritdoc />
    public int Count => _failures.Count;

    /// <inheritdoc />
    public ValidationFailure this[int index] => _failures[index];

    /// <summary>Adds an error at the current path scope.</summary>
    public void Add(IDomainError error) => AddAt(string.Empty, error);

    /// <summary>Adds an error at <paramref name="relativePath"/> below the current path scope.</summary>
    public void AddAt(string relativePath, IDomainError error)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(error);
        _failures.Add(new ValidationFailure(CombinePath(_currentPath, relativePath), error));
    }

    /// <summary>Adds a failure, treating its path as relative to the current path scope.</summary>
    public void Add(ValidationFailure failure)
        => _failures.Add(failure with { Path = CombinePath(_currentPath, failure.Path) });

    /// <summary>Copies all failures from <paramref name="other"/>, prefixing them with the current path scope.</summary>
    public void Merge(ValidationErrors other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var failure in other._failures)
        {
            Add(failure);
        }
    }

    /// <summary>
    /// Copies all failures from <paramref name="other"/>, prefixing them with the current
    /// path scope followed by <paramref name="relativePath"/>.
    /// </summary>
    public void Merge(string relativePath, ValidationErrors other)
    {
        using (PushPath(relativePath))
        {
            Merge(other);
        }
    }

    /// <summary>Whether any failure carries an error of type <typeparamref name="TError"/>.</summary>
    public bool Contains<TError>()
        where TError : IDomainError
    {
        foreach (var failure in _failures)
        {
            if (failure.Error is TError)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The failures whose error is of type <typeparamref name="TError"/>.</summary>
    public IEnumerable<ValidationFailure> OfType<TError>()
        where TError : IDomainError
    {
        foreach (var failure in _failures)
        {
            if (failure.Error is TError)
            {
                yield return failure;
            }
        }
    }

    /// <summary>
    /// Pushes a path segment onto the current scope. Dispose the returned scope to restore
    /// the previous path. Segments starting with <c>[</c> are treated as indexers and joined
    /// without a dot.
    /// </summary>
    public PathScope PushPath(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var previous = _currentPath;
        _currentPath = CombinePath(previous, segment);
        return new PathScope(this, previous);
    }

    /// <summary>
    /// Joins two path fragments using the JSON path convention: <c>a</c> + <c>b</c> is
    /// <c>a.b</c>, while <c>a</c> + <c>[2]</c> is <c>a[2]</c>. Empty fragments pass the
    /// other fragment through unchanged.
    /// </summary>
    public static string CombinePath(string prefix, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return prefix;
        }

        if (string.IsNullOrEmpty(prefix))
        {
            return relativePath;
        }

        return relativePath[0] == '[' ? prefix + relativePath : prefix + "." + relativePath;
    }

    /// <inheritdoc />
    public IEnumerator<ValidationFailure> GetEnumerator() => _failures.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Restores the previous path scope when disposed. See <see cref="PushPath"/>.</summary>
    public readonly struct PathScope : IDisposable
    {
        private readonly ValidationErrors _owner;
        private readonly string _previous;

        internal PathScope(ValidationErrors owner, string previous)
        {
            _owner = owner;
            _previous = previous;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_owner is not null)
            {
                _owner._currentPath = _previous;
            }
        }
    }
}
