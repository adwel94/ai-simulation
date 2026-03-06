"""LLM provider factory. Creates the appropriate LangChain chat model based on settings."""

from langchain_core.language_models import BaseChatModel

from src.config import settings


def create_llm() -> BaseChatModel:
    """Create a chat model instance based on the configured provider.

    Reads provider, credentials, and model name from settings (.env).
    Supports:
        - "gemini": Google Gemini via langchain-google-genai
        - "openai": OpenAI-compatible API (also works with vLLM)
    """
    provider = settings.llm_provider

    if provider == "gemini":
        from langchain_google_genai import ChatGoogleGenerativeAI

        return ChatGoogleGenerativeAI(
            model=settings.model_name,
            google_api_key=settings.google_api_key,
            temperature=settings.temperature,
        )
    elif provider == "openai":
        from langchain_openai import ChatOpenAI

        kwargs = {
            "model": settings.model_name,
            "temperature": settings.temperature,
        }
        if settings.openai_api_key:
            kwargs["api_key"] = settings.openai_api_key
        if settings.openai_base_url:
            kwargs["base_url"] = settings.openai_base_url
        return ChatOpenAI(**kwargs)
    else:
        raise ValueError(
            f"Unknown LLM provider: '{provider}'. "
            f"Set LLM_PROVIDER to 'gemini' or 'openai' in .env"
        )
