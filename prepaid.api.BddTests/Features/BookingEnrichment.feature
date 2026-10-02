Feature: Booking enrichment from the customer directory

  Scenario: Booking retrieval enriched with customer profile
    Given a customer profile exists for "Jane Smith" with email "jane.smith@example.com" and loyalty tier "Gold"
    When I insert a booking with id "20", customer "Jane Smith" and amount 50
    And I request booking "20"
    Then the response status should be 200
    And the customer email should be "jane.smith@example.com"

  Scenario: Customer directory unavailable degrades gracefully
    Given the customer directory service is unavailable
    When I insert a booking with id "21", customer "Bob Lee" and amount 75
    And I request booking "21"
    Then the response status should be 200
    And no customer profile should be returned
