"""LLM provider factory. Creates the appropriate LangChain chat model based on settings."""

import logging

from langchain_core.language_models import BaseChatModel

from src.config import settings

logger = logging.getLogger(__name__)


def create_llm(
    provider: str | None = None,
    model_name: str | None = None,
    base_url: str | None = None,
) -> BaseChatModel:
    """Create a chat model instance.

    Args:
        provider: Override settings.llm_provider ("gemini" or "openai").
        model_name: Override settings.model_name.
        base_url: Override settings.openai_base_url (for vLLM etc.).
    """
    provider = provider or settings.llm_provider
    model = model_name or settings.model_name
    logger.info(f"Creating LLM: provider={provider}, model={model}")

    if provider == "gemini":
        from langchain_google_genai import ChatGoogleGenerativeAI

        return ChatGoogleGenerativeAI(
            model=model,
            google_api_key=settings.google_api_key,
            temperature=settings.temperature,
        )
    elif provider == "openai":
        from langchain_openai import ChatOpenAI

        kwargs = {
            "model": model,
            "temperature": settings.temperature,
        }
        api_key = settings.openai_api_key or "EMPTY"
        kwargs["api_key"] = api_key

        url = base_url or settings.openai_base_url
        if url:
            kwargs["base_url"] = url
        return ChatOpenAI(**kwargs)
    else:
        logger.error(f"Unknown LLM provider: '{provider}'")
        raise ValueError(
            f"Unknown LLM provider: '{provider}'. "
            f"Set LLM_PROVIDER to 'gemini' or 'openai' in .env"
        )
