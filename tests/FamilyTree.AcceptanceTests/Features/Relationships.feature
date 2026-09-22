Feature: Relationships between people

Scenario: Linking a child to a parent does not create an extra commit on the parent's file
    Given a person "John Doe" born in 1920 exists
    And a person "Jane Doe" born in 1952 exists
    When I set "John Doe" as a parent of "Jane Doe"
    Then "John Doe"'s details page lists "Jane Doe" as a child
    And "John Doe" has exactly 1 commit

Scenario: Linking a spouse relationship commits both files together
    Given a person "Robert Doe" born in 1950 exists
    And a person "Jane Doe" born in 1952 exists
    When I link "Robert Doe" and "Jane Doe" as spouses married on "1974-06-02"
    Then "Jane Doe"'s details page shows "Robert Doe" as a spouse
    And "Robert Doe"'s details page shows "Jane Doe" as a spouse
    And "Robert Doe" has exactly 2 commits
    And "Jane Doe" has exactly 2 commits

Scenario: Removing a spouse relationship unlinks both people
    Given a person "Robert Doe" born in 1950 exists
    And a person "Jane Doe" born in 1952 exists
    And "Robert Doe" and "Jane Doe" are linked as spouses married on "1974-06-02"
    When I remove the spouse link between "Robert Doe" and "Jane Doe"
    Then "Jane Doe"'s details page does not show "Robert Doe" as a spouse
    And "Robert Doe"'s details page does not show "Jane Doe" as a spouse

Scenario: Deleting a person who is referenced elsewhere is blocked
    Given a person "John Doe" born in 1920 exists
    And a person "Jane Doe" born in 1952 exists
    And I set "John Doe" as a parent of "Jane Doe"
    When I attempt to delete "John Doe"
    Then the deletion is blocked
    And "John Doe" still exists
