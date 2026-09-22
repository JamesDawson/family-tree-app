Feature: Person management

Scenario: Creating a person writes a file and commits it
    When I create a person "John Doe" born in 1920
    Then "John Doe" exists
    And "John Doe" has exactly 1 commit

Scenario: Editing a person's notes creates a new commit
    Given a person "John Doe" born in 1920 exists
    When I update "John Doe"'s notes to "A note about John."
    Then "John Doe"'s details page shows the notes "A note about John."
    And "John Doe" has exactly 2 commits

Scenario: Deleting an unreferenced person removes them
    Given a person "John Doe" born in 1920 exists
    When I attempt to delete "John Doe"
    Then "John Doe" no longer exists
