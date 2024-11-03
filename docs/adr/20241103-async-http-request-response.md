# async-http-request-response

- Deciders: [list everyone involved in the decision] <!-- optional -->
- Date: [YYYY-MM-DD when the decision was last updated] <!-- optional. To customize the ordering without relying on Git creation dates and filenames -->
- Tags: [space and/or comma separated list of tags] <!-- optional -->

Technical Story: https://berkayyerdelen.atlassian.net/browse/KAN-19 <!-- optional -->

## Context and Problem Statement

As a developer I'd like to see that what are the advantages of async http calls by using rabbitmq as message broker.

## Decision Drivers <!-- optional -->

Created simple endpoint that publishes message to broker and return it. It uses built-in correlation id to trace request. You can use http file to hit endpoint.


## Decision Outcome

-  This architecture mainly benefits backend reliablility and fault tolerance rather than directly enchancing user experience if an immediate response is critical.
-  Reliability: Even if a service instance fails during request, the message can be routed to another consumer, ensuring the request does not fail outright.
-  Resilence to system failures : Messages are queued and retried if temporary issues occur with consumers, avoiding requests
-  Scalability: RabbitMq can distrubute the load across multiple consumers, which is beneficialin high-traffic systems where direct http calls might struggle.
-  Monitoring and Auditing


### Positive Consequences <!-- optional -->

-  Having one publisher and one consumer processes each message, so you avoid managing multiple responses for the same request
-  Predictable Scalling : With one consumer per request, you can scale both sides independently as needed(e.g., adding more consumers for higher throughput)
-  Microservice communication: Each service can independently request data or actions from others without needing direct connections. To avoud message duplication, you can use a direct exchange setup where messages are routed to specific queues with single consumers.

### Negative Consequences <!-- optional -->

-  Duplicate Processing : Each queue bound to the fanout exchange will receive the same message, leading to multuple responses for a single request.



## Links <!-- optional -->

- https://www.developertoarchitect.com/lessons/lesson1.html
