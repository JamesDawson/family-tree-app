Feature: History and rollback

Scenario: The app initializes a fresh data repository on first run
    Then the data directory is a git repository with exactly 1 commit

Scenario: Reverting a person to a prior commit adds a new commit rather than rewriting history
    Given a person "Jane Doe" born in 1952 exists
    And I update "Jane Doe"'s notes to "Changed notes."
    When I revert "Jane Doe" to their first commit
    Then "Jane Doe" has exactly 3 commits
    And "Jane Doe"'s notes are empty again
