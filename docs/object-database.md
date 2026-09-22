# Lattice In-Memory Object Database

- Database can store objects of different Types (domain models), and collections of those types using build-in .NET data structures (probably immutable collections)
    - The collections hold references to the objects within them, but cannot change those objects, as they are each owned by a different actor.  The actor owning the collection can only perform collection-level commands such.
- The object database exposes a native RESTful API, where each object is addressed by a path, making it easily accessible via the REST API, such as:
    - /orders : The collection of all orders (e.g. an immutable dictionary)
    - /orders/{id} : A specific order in the dictionary
- Each object in the database is maintained by a dedicated Actor from the `Typewright.Actor` framework.  That actor owns the state of the object, and is the only thing that can update the state.  
    - Requests sent to the API for a single object (e.g. PUT, PATCH, etc.) are queued on the actor's mailbox for execution, so no locking is required, as the actor only processes one command at a time.  Different objects can be updated in parallel because they're owned by different actors, but all commands for a single object are inherently serialized in the actor's mailbox so the execute one-at-a-time.
    - Requests sent to the API for a collection object (e.g. POST, DELETE, etc.) are similar queued on the collection actor's mailbox, ensuring similar concurrency guarantees.
- GET operations can quickly access the current state of the actor without going through the mailbox (if the request headers do not specify `read-persistent`).
    - In general, CQRS is used to allow read operations to be fast and not be blocked by write operations.
- Only the current state of a given object is available in memory, but an append-only log of all commands for each object can be persisted (command-sourcing) to retain and re-create previous states.
