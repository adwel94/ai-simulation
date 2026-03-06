from langgraph.graph import StateGraph, END

from src.agent.nodes import observe, think, act, check_done
from src.agent.state import ClawState


def build_graph() -> StateGraph:
    """Build the LangGraph agent graph.

    Flow: START -> observe -> think -> act -> check
                                                ├─ continue -> observe
                                                └─ end -> END
    """
    graph = StateGraph(ClawState)

    graph.add_node("observe", observe)
    graph.add_node("think", think)
    graph.add_node("act", act)

    graph.set_entry_point("observe")
    graph.add_edge("observe", "think")
    graph.add_edge("think", "act")

    graph.add_conditional_edges(
        "act",
        check_done,
        {
            "continue": "observe",
            "end": END,
        },
    )

    return graph.compile()
