Feature: Booking and refund flow

  Scenario: Insert a booking, retrieve it, then refund its payment
    Given a payment exists with transaction id "11111111-1111-1111-1111-111111111111" and amount 100
    When I insert a booking with id "10", customer "John Doe" and amount 100
    And I request booking "10"
    Then the response status should be 200
    And the customer name should be "John Doe"
    When I refund 25 for transaction "11111111-1111-1111-1111-111111111111"
    Then the response status should be 200
    And the refund should be successful
    And the refund amount should be 25
